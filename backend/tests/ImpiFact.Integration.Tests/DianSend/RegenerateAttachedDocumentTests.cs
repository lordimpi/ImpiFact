using System.Text;
using System.Xml;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Features.Documents.Maintenance;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// Mensaje <c>RegenerateAttachedDocument</c> en el Worker (ruta <c>regenerate-attached-document</c>): arma y firma otra
/// revisión del AttachedDocument con el XML firmado y el ApplicationResponse guardados; sin ellos, falla sin reintentos.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class RegenerateAttachedDocumentTests
{
    [Test]
    public async Task Stores_a_new_signed_revision_built_from_the_stored_application_response()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));
        await host.RunOutboxAsync();
        await DoneWithPendingAsync(host);
        var calls = host.Dian.Requests.Count;

        var messageId = await EnqueueAsync(host, document);
        await host.RunOutboxAsync();

        var attached = (await host.FilesAsync(document.Id)).Where(file => file.Kind == DocumentFileKind.AttachedDocument).ToList();
        var xml = new XmlDocument { PreserveWhitespace = true };
        xml.LoadXml(Encoding.UTF8.GetString(await host.ReadFileAsync(attached[^1])));
        var ns = new XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("cbc", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2");
        ns.AddNamespace("cac", "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2");
        Assert.Multiple(async () =>
        {
            Assert.That(attached.Select(file => file.Revision), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(attached[^1].ContentType, Is.EqualTo("application/xml"));
            Assert.That(xml.DocumentElement!.LocalName, Is.EqualTo("AttachedDocument"));
            Assert.That(xml.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#"), Has.Count.EqualTo(1));
            Assert.That(xml.SelectSingleNode("//cac:ParentDocumentLineReference//cbc:Description", ns)?.InnerText,
                Is.EqualTo(DianReplies.ApplicationResponseXml));
            Assert.That(xml.SelectSingleNode("/*/cbc:ParentDocumentID", ns)?.InnerText, Is.EqualTo(document.FullNumber));
            Assert.That((await MessageAsync(host, messageId)).Status, Is.EqualTo(OutboxStatus.Done));
            Assert.That(host.Dian.Requests, Has.Count.EqualTo(calls), "Regenerating never calls the DIAN.");
        });
    }

    [Test]
    public async Task Without_the_application_response_the_message_fails_at_once()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer, status: DocumentStatus.Accepted);

        var messageId = await EnqueueAsync(host, document);
        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((await MessageAsync(host, messageId)).Status, Is.EqualTo(OutboxStatus.Failed));
            Assert.That((await host.FilesAsync(document.Id)).Select(file => file.Kind), Does.Not.Contain(DocumentFileKind.AttachedDocument));
        });
    }

    [Test]
    public async Task A_document_the_dian_did_not_accept_is_never_regenerated()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer, status: DocumentStatus.Rejected);
        await using (var scope = host.OpenScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDocumentFiles>().AddAsync(
                new NewDocumentFile(issuer.CompanyId, document.Id, DocumentFileKind.ApplicationResponse,
                    Encoding.UTF8.GetBytes(DianReplies.ApplicationResponseXml), "application/xml"), CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().SaveChangesAsync();
        }

        var messageId = await EnqueueAsync(host, document);
        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((await MessageAsync(host, messageId)).Status, Is.EqualTo(OutboxStatus.Failed));
            Assert.That((await host.FilesAsync(document.Id)).Select(file => file.Kind), Does.Not.Contain(DocumentFileKind.AttachedDocument));
        });
    }

    private static async Task<Guid> EnqueueAsync(DianSendHost host, Document document)
    {
        await using var scope = host.OpenScope();
        var id = await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(
            OutboxMessageTypes.RegenerateAttachedDocument, new RegenerateAttachedDocumentPayload(document.Id), document.CompanyId, null,
            CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().SaveChangesAsync();
        return id;
    }

    /// <summary>Marca hechos los mensajes que dejó la aceptación (el PDF), para que la tanda siguiente solo tome el de la prueba.</summary>
    private static async Task DoneWithPendingAsync(DianSendHost host)
    {
        await using var scope = host.OpenScope();
        await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().Set<OutboxMessage>()
            .Where(message => message.Status == OutboxStatus.Pending)
            .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.Status, OutboxStatus.Done));
    }

    private static async Task<OutboxMessage> MessageAsync(DianSendHost host, Guid id)
    {
        await using var scope = host.OpenScope();
        return await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().Set<OutboxMessage>().AsNoTracking().SingleAsync(row => row.Id == id);
    }
}
