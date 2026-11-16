using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Features.Documents.Delivery;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// Al aceptar un documento, el Worker guarda el AttachedDocument (contenedor electrónico que se entrega al comprador)
/// firmado con el certificado de la empresa y encola <c>RenderPdf</c>, en el mismo guardado que la aceptación
/// (Arquitectura detallada, sección 7, paso 9; supuesto C6 de la fase 4). La aceptación nunca falla por el contenedor.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class AttachedDocumentOnAcceptanceTests
{
    [Test]
    public async Task An_accepted_invoice_stores_a_signed_attached_document_and_queues_its_pdf()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));

        await host.RunOutboxAsync();

        var files = await host.FilesAsync(document.Id);
        var attached = Encoding.UTF8.GetString(await host.ReadFileAsync(files.Single(file => file.Kind == DocumentFileKind.AttachedDocument)));
        var signedXml = Encoding.UTF8.GetString(await host.ReadFileAsync(files.Single(file => file.Kind == DocumentFileKind.SignedXml)));
        var pdf = (await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.RenderPdf)).Single();
        var (doc, ns) = Load(attached);
        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That(files.Single(file => file.Kind == DocumentFileKind.AttachedDocument).Revision, Is.EqualTo(1));
            Assert.That(files.Single(file => file.Kind == DocumentFileKind.AttachedDocument).ContentType, Is.EqualTo("application/xml"));
            Assert.That(VerifiesWith(doc, ns, await ThumbprintAsync(host, issuer)), Is.True);
            Assert.That(doc.SelectSingleNode("/ad:AttachedDocument/cbc:ID", ns)?.InnerText, Is.EqualTo(document.Cufe));
            Assert.That(doc.SelectSingleNode("/ad:AttachedDocument/cbc:ParentDocumentID", ns)?.InnerText, Is.EqualTo(document.FullNumber));
            Assert.That(doc.SelectSingleNode("/ad:AttachedDocument/cac:Attachment//cbc:Description", ns)?.InnerText, Is.EqualTo(signedXml));
            Assert.That(doc.SelectSingleNode("//cac:ParentDocumentLineReference//cbc:Description", ns)?.InnerText,
                Is.EqualTo(DianReplies.ApplicationResponseXml));
            Assert.That(doc.SelectSingleNode("//cac:ReceiverParty//cbc:CompanyID", ns)?.InnerText, Is.EqualTo("222222222222"));
            Assert.That(pdf.Status, Is.EqualTo(OutboxStatus.Pending));
            Assert.That(pdf.Payload["document_id"]?.GetValue<Guid>(), Is.EqualTo(document.Id));
        });
    }

    [Test]
    public async Task A_document_accepted_by_a_status_poll_gets_its_attached_document_too()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer, status: DocumentStatus.Unconfirmed, sentAt: host.Clock.UtcNow);
        await host.EnqueuePollAsync(document, DianStatusQuery.GetStatus);
        host.Dian.Reply(DianReplies.GetStatus(true, "00", DianReplies.Notification));

        await host.RunOutboxAsync();

        var files = await host.FilesAsync(document.Id);
        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.AcceptedWithWarnings));
            Assert.That(files.Count(file => file.Kind == DocumentFileKind.AttachedDocument), Is.EqualTo(1));
            Assert.That(await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.RenderPdf), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task An_acceptance_without_the_application_response_stays_accepted_and_still_queues_the_pdf()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer, status: DocumentStatus.Unconfirmed, sentAt: host.Clock.UtcNow);
        await host.EnqueuePollAsync(document, DianStatusQuery.GetStatus);
        host.Dian.Reply(DianReplies.GetStatusWithoutApplicationResponse(true, "00"));

        await host.RunOutboxAsync();

        var stored = await host.DocumentAsync(document.Id);
        var files = await host.FilesAsync(document.Id);
        Assert.Multiple(async () =>
        {
            Assert.That(stored.Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That(stored.AcceptedAt, Is.EqualTo(host.Clock.UtcNow));
            Assert.That(files.Select(file => file.Kind), Does.Not.Contain(DocumentFileKind.AttachedDocument));
            Assert.That(files.Select(file => file.Kind), Does.Not.Contain(DocumentFileKind.ApplicationResponse));
            Assert.That(await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.RenderPdf), Has.Count.EqualTo(1));
            Assert.That((await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus)).Single().Status, Is.EqualTo(OutboxStatus.Done));
        });
    }

    [Test]
    public async Task A_rejected_document_gets_no_attached_document_and_no_pdf()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(false, "99", DianReplies.Rejection));

        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Rejected));
            Assert.That((await host.FilesAsync(document.Id)).Select(file => file.Kind), Does.Not.Contain(DocumentFileKind.AttachedDocument));
            Assert.That(await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.RenderPdf), Is.Empty);
        });
    }

    private static (XmlDocument Doc, XmlNamespaceManager Ns) Load(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("ad", "urn:oasis:names:specification:ubl:schema:xsd:AttachedDocument-2");
        ns.AddNamespace("cbc", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2");
        ns.AddNamespace("cac", "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2");
        ns.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");
        return (doc, ns);
    }

    /// <summary>La firma verifica y el certificado que trae es el de la empresa (por su huella).</summary>
    private static bool VerifiesWith(XmlDocument doc, XmlNamespaceManager ns, string thumbprint)
    {
        var checker = new SignedXml(doc);
        checker.LoadXml((XmlElement)doc.SelectSingleNode("//ds:Signature", ns)!);
        var certificate = checker.KeyInfo.OfType<KeyInfoX509Data>().SelectMany(data => data.Certificates!.Cast<System.Security.Cryptography.X509Certificates.X509Certificate2>()).Single();
        return checker.CheckSignature(certificate, verifySignatureOnly: true)
            && string.Equals(certificate.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ThumbprintAsync(DianSendHost host, TestIssuer issuer)
    {
        await using var scope = host.OpenScope();
        return await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().Set<Certificate>()
            .IgnoreQueryFilters([QueryFilters.Tenant]).Where(row => row.CompanyId == issuer.CompanyId)
            .Select(row => row.Thumbprint).SingleAsync();
    }
}
