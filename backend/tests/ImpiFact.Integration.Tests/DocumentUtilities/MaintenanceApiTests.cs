using System.Text;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Integration.Tests.DianSend;
using static ImpiFact.Integration.Tests.DocumentUtilities.UtilitiesHost;

namespace ImpiFact.Integration.Tests.DocumentUtilities;

/// <summary>
/// <c>POST /api/v1/documents/{id}/pdf/regenerate</c>, <c>/attached-document/regenerate</c> y <c>/email</c> (el
/// <c>regeneratepdf</c>, <c>regenerate-attached-document</c> y <c>send-email</c> del PHP, carpetas 05 y 10 del Postman):
/// responden de inmediato y dejan el trabajo en la bandeja de salida para el Worker (decisión C5), solo para documentos
/// aceptados de la empresa de la credencial.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class MaintenanceApiTests
{
    private const string EmailRequest = """{"sendmail":true,"number":1}""";

    [Test]
    public async Task Regenerate_pdf_queues_the_work_and_answers_at_once()
    {
        var company = await CompanyAsync(withCertificate: false);
        var client = await ClientAsync(company, ApiScopes.DocumentsWrite);
        var document = await DocumentAsync(company);

        var (status, body) = await SendAsync(client, HttpMethod.Post, $"/api/v1/documents/{document.Id}/pdf/regenerate");

        var message = (await MessagesAsync(company.Id, OutboxMessageTypes.RegeneratePdf)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(200), body.ToString());
            Assert.That(body.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(body.GetProperty("message").GetString(), Is.EqualTo($"La regeneración del PDF del documento {document.FullNumber} quedó en cola."));
            Assert.That(body.GetProperty("document").GetProperty("id").GetGuid(), Is.EqualTo(document.Id));
            Assert.That(body.GetProperty("document").GetProperty("status").GetString(), Is.EqualTo("Accepted"));
            Assert.That(DocumentIdOf(message), Is.EqualTo(document.Id));
            Assert.That(message.Status, Is.EqualTo(OutboxStatus.Pending));
        });
    }

    [TestCase(DocumentStatus.Queued)]
    [TestCase(DocumentStatus.Rejected)]
    [TestCase(DocumentStatus.Unconfirmed)]
    public async Task A_document_the_dian_did_not_accept_is_not_regenerated(DocumentStatus documentStatus)
    {
        var company = await CompanyAsync(withCertificate: false);
        var client = await ClientAsync(company, ApiScopes.DocumentsWrite);
        var document = await DocumentAsync(company, documentStatus);

        var (status, body) = await SendAsync(client, HttpMethod.Post, $"/api/v1/documents/{document.Id}/pdf/regenerate");

        Assert.Multiple(async () =>
        {
            Assert.That(status, Is.EqualTo(409));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("document.not_accepted"));
            Assert.That(await MessagesAsync(company.Id, OutboxMessageTypes.RegeneratePdf), Is.Empty);
        });
    }

    [Test]
    public async Task Another_company_documents_are_never_regenerated_nor_emailed()
    {
        var company = await CompanyAsync();
        var other = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsWrite);
        var foreign = await DocumentAsync(other, requestJson: EmailRequest);
        await FileAsync(foreign, DocumentFileKind.ApplicationResponse, Encoding.UTF8.GetBytes(DianReplies.ApplicationResponseXml), "application/xml");

        var (pdf, _) = await SendAsync(client, HttpMethod.Post, $"/api/v1/documents/{foreign.Id}/pdf/regenerate");
        var (attached, _) = await SendAsync(client, HttpMethod.Post, $"/api/v1/documents/{foreign.Id}/attached-document/regenerate");
        var (email, _) = await SendAsync(client, HttpMethod.Post, $"/api/v1/documents/{foreign.Id}/email");

        Assert.Multiple(async () =>
        {
            Assert.That(pdf, Is.EqualTo(404));
            Assert.That(attached, Is.EqualTo(404));
            Assert.That(email, Is.EqualTo(404));
            Assert.That(await MessagesAsync(company.Id, OutboxMessageTypes.SendEmail), Is.Empty);
            Assert.That(await MessagesAsync(other.Id, OutboxMessageTypes.SendEmail), Is.Empty);
            Assert.That(await MessagesAsync(other.Id, OutboxMessageTypes.RegeneratePdf), Is.Empty);
            Assert.That(await MessagesAsync(other.Id, OutboxMessageTypes.RegenerateAttachedDocument), Is.Empty);
        });
    }

    [Test]
    public async Task Regenerate_attached_document_queues_the_work_when_the_application_response_is_stored()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsWrite);
        var document = await DocumentAsync(company);
        await FileAsync(document, DocumentFileKind.ApplicationResponse, Encoding.UTF8.GetBytes(DianReplies.ApplicationResponseXml), "application/xml");

        var (status, body) = await SendAsync(client, HttpMethod.Post, $"/api/v1/documents/{document.Id}/attached-document/regenerate");

        var queued = await MessagesAsync(company.Id, OutboxMessageTypes.RegenerateAttachedDocument);
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(200), body.ToString());
            Assert.That(body.GetProperty("message").GetString(),
                Is.EqualTo($"La regeneración del AttachedDocument del documento {document.FullNumber} quedó en cola."));
            Assert.That(queued.Select(DocumentIdOf), Is.EqualTo(new[] { document.Id }));
        });
    }

    [Test]
    public async Task Regenerate_attached_document_without_the_application_response_or_the_certificate_is_refused()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsWrite);
        var withoutResponse = await DocumentAsync(company);
        var uncertified = await CompanyAsync(withCertificate: false);
        var uncertifiedClient = await ClientAsync(uncertified, ApiScopes.DocumentsWrite);
        var withResponse = await DocumentAsync(uncertified);
        await FileAsync(withResponse, DocumentFileKind.ApplicationResponse, Encoding.UTF8.GetBytes(DianReplies.ApplicationResponseXml), "application/xml");

        var (missing, body) = await SendAsync(client, HttpMethod.Post, $"/api/v1/documents/{withoutResponse.Id}/attached-document/regenerate");
        var (noCertificate, certificateBody) = await SendAsync(
            uncertifiedClient, HttpMethod.Post, $"/api/v1/documents/{withResponse.Id}/attached-document/regenerate");

        Assert.Multiple(async () =>
        {
            Assert.That(missing, Is.EqualTo(409));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("document.application_response_missing"));
            Assert.That(noCertificate, Is.EqualTo(422));
            Assert.That(certificateBody.GetProperty("code").GetString(), Is.EqualTo("certificate.missing"));
            Assert.That(await MessagesAsync(company.Id, OutboxMessageTypes.RegenerateAttachedDocument), Is.Empty);
            Assert.That(await MessagesAsync(uncertified.Id, OutboxMessageTypes.RegenerateAttachedDocument), Is.Empty);
        });
    }

    [Test]
    public async Task Send_email_queues_the_email_again_even_if_it_was_sent()
    {
        var company = await CompanyAsync(withCertificate: false);
        var client = await ClientAsync(company, ApiScopes.DocumentsWrite);
        var document = await DocumentAsync(company, requestJson: EmailRequest, emailStatus: EmailStatus.Sent);

        var (status, body) = await SendAsync(client, HttpMethod.Post, $"/api/v1/documents/{document.Id}/email");

        var message = (await MessagesAsync(company.Id, OutboxMessageTypes.SendEmail)).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(status, Is.EqualTo(200), body.ToString());
            Assert.That(body.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(body.GetProperty("message").GetString(), Is.EqualTo($"El envío del correo del documento {document.FullNumber} quedó en cola."));
            Assert.That(DocumentIdOf(message), Is.EqualTo(document.Id));
            Assert.That((await ReloadAsync(document)).EmailStatus, Is.EqualTo(EmailStatus.NotSent));
        });
    }

    [Test]
    public async Task Send_email_of_a_document_issued_without_sendmail_is_refused_and_changes_nothing()
    {
        var company = await CompanyAsync(withCertificate: false);
        var client = await ClientAsync(company, ApiScopes.DocumentsWrite);
        var document = await DocumentAsync(company, requestJson: """{"sendmail":false}""", emailStatus: EmailStatus.Failed);

        var (status, body) = await SendAsync(client, HttpMethod.Post, $"/api/v1/documents/{document.Id}/email");

        Assert.Multiple(async () =>
        {
            Assert.That(status, Is.EqualTo(422));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("email.not_requested"));
            Assert.That(await MessagesAsync(company.Id, OutboxMessageTypes.SendEmail), Is.Empty);
            Assert.That((await ReloadAsync(document)).EmailStatus, Is.EqualTo(EmailStatus.Failed));
        });
    }

    [Test]
    public async Task Send_email_of_a_document_the_dian_did_not_accept_is_refused()
    {
        var company = await CompanyAsync(withCertificate: false);
        var client = await ClientAsync(company, ApiScopes.DocumentsWrite);
        var document = await DocumentAsync(company, DocumentStatus.Rejected, requestJson: EmailRequest);

        var (status, body) = await SendAsync(client, HttpMethod.Post, $"/api/v1/documents/{document.Id}/email");

        Assert.Multiple(async () =>
        {
            Assert.That(status, Is.EqualTo(409));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("document.not_accepted"));
            Assert.That(await MessagesAsync(company.Id, OutboxMessageTypes.SendEmail), Is.Empty);
        });
    }

    [Test]
    public async Task The_maintenance_routes_need_the_documents_write_scope()
    {
        var company = await CompanyAsync(withCertificate: false);
        var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var document = await DocumentAsync(company);

        var (pdf, _) = await SendAsync(client, HttpMethod.Post, $"/api/v1/documents/{document.Id}/pdf/regenerate");
        var (email, _) = await SendAsync(client, HttpMethod.Post, $"/api/v1/documents/{document.Id}/email");

        Assert.Multiple(() =>
        {
            Assert.That(pdf, Is.EqualTo(403));
            Assert.That(email, Is.EqualTo(403));
        });
    }
}
