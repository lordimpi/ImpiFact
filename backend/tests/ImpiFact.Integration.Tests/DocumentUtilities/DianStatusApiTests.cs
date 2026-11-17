using System.Text;
using System.Text.Json;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.DianSend;
using static ImpiFact.Integration.Tests.DocumentUtilities.UtilitiesHost;

namespace ImpiFact.Integration.Tests.DocumentUtilities;

/// <summary>
/// <c>status/document</c>, <c>status/zip</c> y <c>xml/document</c> (carpetas 03 y 05 del Postman): responden con lo que
/// guardó el Worker, con la forma del PHP, sin llamar a la DIAN.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class DianStatusApiTests
{
    [Test]
    public async Task Status_document_answers_the_latest_stored_dian_answer_in_the_php_shape()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var document = await DocumentAsync(company);
        await SubmissionAsync(document, 1, DianOperation.SendBillSync, DianReplies.SendBillSync(false, "99", DianReplies.Rejection));
        var accepted = DianReplies.GetStatus(true, "00");
        await SubmissionAsync(document, 2, DianOperation.GetStatus, accepted);
        await SubmissionAsync(document, 3, DianOperation.GetStatus, responseBody: null);
        var calls = UtilitiesHost.Api.Dian.Requests.Count;

        var (status, body) = await SendAsync(client, HttpMethod.Post, $"/api/ubl2.1/status/document/{document.Cufe}", """{"sendmail":true}""");

        var result = body.GetProperty("ResponseDian").GetProperty("Envelope").GetProperty("Body").GetProperty("GetStatusResponse")
            .GetProperty("GetStatusResult");
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(200), body.ToString());
            Assert.That(body.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(body.GetProperty("message").GetString(), Is.EqualTo("Consulta generada con éxito"));
            Assert.That(result.GetProperty("IsValid").GetString(), Is.EqualTo("true"));
            Assert.That(result.GetProperty("StatusCode").GetString(), Is.EqualTo("00"));
            Assert.That(body.GetProperty("rptazip").GetString(), Is.EqualTo(Convert.ToBase64String(Encoding.UTF8.GetBytes(accepted))));
            Assert.That(body.GetProperty("reqzip").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(body.GetProperty("document").GetRawText(), Is.EqualTo(
                $$"""{"id":"{{document.Id}}","document_type_id":1,"full_number":"{{document.FullNumber}}","status":"Accepted","state_document_id":1,"cufe":"{{document.Cufe}}"}"""));
            Assert.That(UtilitiesHost.Api.Dian.Requests, Has.Count.EqualTo(calls), "The API must not call the DIAN.");
        });
    }

    /// <summary>
    /// Como el PHP (ejemplo de la carpeta 03 del Postman), las dos consultas de estado responden también los días que
    /// le quedan al certificado y el AttachedDocument (contenedor que se entrega al comprador) en base64, o nulo si
    /// todavía no existe.
    /// </summary>
    [Test]
    public async Task Status_document_and_zip_answer_the_certificate_days_and_the_attached_document()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var accepted = await DocumentAsync(company);
        await SubmissionAsync(accepted, 1, DianOperation.SendBillSync, DianReplies.SendBillSync(true, "00"));
        var attached = Encoding.UTF8.GetBytes("<AttachedDocument/>");
        await FileAsync(accepted, DocumentFileKind.AttachedDocument, attached, "application/xml");
        var testSet = await DocumentAsync(company, DocumentStatus.Sent);
        await SubmissionAsync(testSet, 1, DianOperation.SendTestSetAsync, DianReplies.SendTestSetAsync("zip-attached"), zipKey: "zip-attached");

        var (documentStatus, document) = await SendAsync(client, HttpMethod.Post, $"/api/ubl2.1/status/document/{accepted.Cufe}");
        var (zipStatus, zip) = await SendAsync(client, HttpMethod.Post, "/api/ubl2.1/status/zip/zip-attached");

        Assert.Multiple(() =>
        {
            Assert.That(documentStatus, Is.EqualTo(200), document.ToString());
            Assert.That(document.GetProperty("certificate_days_left").GetInt32(), Is.EqualTo(3637));
            Assert.That(document.GetProperty("attacheddocument").GetString(), Is.EqualTo(Convert.ToBase64String(attached)));
            Assert.That(zipStatus, Is.EqualTo(200), zip.ToString());
            Assert.That(zip.GetProperty("certificate_days_left").GetInt32(), Is.EqualTo(3637));
            Assert.That(zip.GetProperty("attacheddocument").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    public async Task Status_document_without_an_answer_yet_says_so()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var document = await DocumentAsync(company, DocumentStatus.Sent);
        await SubmissionAsync(document, 1, DianOperation.SendTestSetAsync, DianReplies.SendTestSetAsync("zip-1"), zipKey: "zip-1");

        var (status, body) = await SendAsync(client, HttpMethod.Get, $"/api/v1/documents/by-cufe/{document.Cufe}/dian-status");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(200), body.ToString());
            Assert.That(body.GetProperty("message").GetString(),
                Is.EqualTo("La DIAN todavía no ha respondido sobre el documento; consulte de nuevo más tarde."));
            Assert.That(body.GetProperty("ResponseDian").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(body.GetProperty("rptazip").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(body.GetProperty("document").GetProperty("status").GetString(), Is.EqualTo("Sent"));
            Assert.That(body.GetProperty("document").GetProperty("state_document_id").GetInt32(), Is.Zero);
        });
    }

    [Test]
    public async Task Status_document_of_another_company_is_not_found()
    {
        var company = await CompanyAsync();
        var other = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var foreign = await DocumentAsync(other);
        await SubmissionAsync(foreign, 1, DianOperation.SendBillSync, DianReplies.SendBillSync(true, "00"));

        var (status, body) = await SendAsync(client, HttpMethod.Post, $"/api/ubl2.1/status/document/{foreign.Cufe}");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(404));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("document.not_found"));
            Assert.That(body.GetProperty("success").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task Status_zip_answers_the_latest_get_status_zip_answer_of_that_key()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var document = await DocumentAsync(company);
        await SubmissionAsync(document, 1, DianOperation.SendTestSetAsync, DianReplies.SendTestSetAsync("zip-a"), zipKey: "zip-a");
        await SubmissionAsync(document, 2, DianOperation.GetStatusZip, DianReplies.GetStatusZip(false, "98"), zipKey: "zip-a",
            finishedAt: Companies.CompaniesHost.Now.AddMinutes(1));
        var decided = DianReplies.GetStatusZip(true, "00");
        await SubmissionAsync(document, 3, DianOperation.GetStatusZip, decided, zipKey: "zip-a", finishedAt: Companies.CompaniesHost.Now.AddMinutes(2));

        var (status, body) = await SendAsync(client, HttpMethod.Post, "/api/ubl2.1/status/zip/zip-a/C:_temp");
        var (canonical, again) = await SendAsync(client, HttpMethod.Get, "/api/v1/documents/zip/zip-a/dian-status");

        var item = body.GetProperty("ResponseDian").GetProperty("Envelope").GetProperty("Body").GetProperty("GetStatusZipResponse")
            .GetProperty("GetStatusZipResult").GetProperty("DianResponse");
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(200), body.ToString());
            Assert.That(body.GetProperty("message").GetString(), Is.EqualTo("Consulta generada con éxito"));
            Assert.That(item.GetProperty("StatusCode").GetString(), Is.EqualTo("00"));
            Assert.That(body.GetProperty("rptazip").GetString(), Is.EqualTo(Convert.ToBase64String(Encoding.UTF8.GetBytes(decided))));
            Assert.That(body.GetProperty("document").GetProperty("id").GetGuid(), Is.EqualTo(document.Id));
            Assert.That(canonical, Is.EqualTo(200));
            Assert.That(again.GetRawText(), Is.EqualTo(body.GetRawText()));
        });
    }

    [Test]
    public async Task Status_zip_before_the_first_get_status_zip_says_there_is_no_answer_yet()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var document = await DocumentAsync(company, DocumentStatus.Sent);
        await SubmissionAsync(document, 1, DianOperation.SendTestSetAsync, DianReplies.SendTestSetAsync("zip-b"), zipKey: "zip-b");

        var (status, body) = await SendAsync(client, HttpMethod.Post, "/api/ubl2.1/status/zip/zip-b");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(200), body.ToString());
            Assert.That(body.GetProperty("ResponseDian").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(body.GetProperty("document").GetProperty("id").GetGuid(), Is.EqualTo(document.Id));
        });
    }

    [Test]
    public async Task Status_zip_of_an_unknown_or_foreign_key_is_not_found()
    {
        var company = await CompanyAsync();
        var other = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var foreign = await DocumentAsync(other);
        await SubmissionAsync(foreign, 1, DianOperation.GetStatusZip, DianReplies.GetStatusZip(true, "00"), zipKey: "zip-foreign");

        var (unknown, _) = await SendAsync(client, HttpMethod.Post, "/api/ubl2.1/status/zip/zip-unknown");
        var (status, body) = await SendAsync(client, HttpMethod.Post, "/api/ubl2.1/status/zip/zip-foreign");

        Assert.Multiple(() =>
        {
            Assert.That(unknown, Is.EqualTo(404));
            Assert.That(status, Is.EqualTo(404));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("document.not_found"));
        });
    }

    [Test]
    public async Task The_status_routes_need_the_documents_read_scope()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.ConfigWrite);
        var document = await DocumentAsync(company);

        var (status, _) = await SendAsync(client, HttpMethod.Post, $"/api/ubl2.1/status/document/{document.Cufe}");
        var (xml, _) = await SendAsync(client, HttpMethod.Post, $"/api/ubl2.1/xml/document/{document.Cufe}");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(403));
            Assert.That(xml, Is.EqualTo(403));
        });
    }

    [Test]
    public async Task Xml_document_answers_the_signed_xml_like_get_xml_by_document_key()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var document = await DocumentAsync(company, DocumentStatus.AcceptedWithWarnings);

        var (status, body) = await SendAsync(client, HttpMethod.Post, $"/api/ubl2.1/xml/document/{document.Cufe}");
        var (canonical, again) = await SendAsync(client, HttpMethod.Get, $"/api/v1/documents/by-cufe/{document.Cufe}/xml");

        var result = body.GetProperty("ResponseDian").GetProperty("Envelope").GetProperty("Body").GetProperty("GetXmlByDocumentKeyResponse")
            .GetProperty("GetXmlByDocumentKeyResult");
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(200), body.ToString());
            Assert.That(body.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(body.GetProperty("message").GetString(), Is.EqualTo("Consulta generada con éxito"));
            Assert.That(body.GetProperty("certificate_days_left").GetInt32(), Is.EqualTo(3637));
            Assert.That(result.GetProperty("Code").GetString(), Is.EqualTo("100"));
            Assert.That(result.GetProperty("XmlBytesBase64").GetString(),
                Is.EqualTo(Convert.ToBase64String(Encoding.UTF8.GetBytes(DianSendHost.SignedXml(document)))));
            Assert.That(canonical, Is.EqualTo(200));
            Assert.That(again.GetRawText(), Is.EqualTo(body.GetRawText()));
        });
    }

    [Test]
    public async Task Xml_document_of_a_document_the_dian_did_not_accept_is_refused()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var document = await DocumentAsync(company, DocumentStatus.Rejected);

        var (status, body) = await SendAsync(client, HttpMethod.Post, $"/api/ubl2.1/xml/document/{document.Cufe}");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(409));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("document.not_accepted"));
            Assert.That(body.GetProperty("message").GetString(), Is.EqualTo($"El documento {document.FullNumber} no ha sido aceptado por la DIAN."));
        });
    }

    [Test]
    public async Task Xml_document_of_another_company_is_not_found()
    {
        var company = await CompanyAsync();
        var other = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var foreign = await DocumentAsync(other);

        var (status, _) = await SendAsync(client, HttpMethod.Post, $"/api/ubl2.1/xml/document/{foreign.Cufe}");

        Assert.That(status, Is.EqualTo(404));
    }
}
