using System.Net;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Features.Documents.Delivery;
using ImpiFact.Dian.Soap;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// The Worker sends the invoices type 04 (type 12) from <c>ContingencyPending</c> with the normal SendBillSync path (phase
/// 7a, T5; php-rules 12.3): accepted, they get the AttachedDocument and the PDF; rejected, they end <c>Rejected</c> with the
/// notice (the PHP left them pending and sent them again forever). While the DIAN does not receive them they stay pending,
/// never <c>Failed</c>: a SOAP Fault leaves a notice, and when the outbox gives up another attempt is scheduled 30 minutes
/// later, during the 48 hours of annex 1.9, section 12.2.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class ContingencySendTests
{
    [Test]
    public async Task An_accepted_pending_type_04_is_sent_with_its_fv_name_and_gets_its_attached_document_and_pdf()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await PendingAsync(host, issuer);
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));

        await host.RunOutboxAsync();

        var stored = await host.DocumentAsync(document.Id);
        var files = await host.FilesAsync(document.Id);
        var zip = files.Single(file => file.Kind == DocumentFileKind.SentZip);
        Assert.Multiple(async () =>
        {
            Assert.That(host.Dian.Requests.Single().Operation, Is.EqualTo("SendBillSync"));
            Assert.That(stored.Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That(stored.SentAt, Is.EqualTo(host.Clock.UtcNow));
            Assert.That(DianSendHost.ZipEntryName(await host.ReadFileAsync(zip)),
                Is.EqualTo(DianFileNames.XmlEntry(DianFileKind.Invoice, issuer.Nit, 2026, 1)));
            Assert.That(files.Select(file => file.Kind), Does.Contain(DocumentFileKind.AttachedDocument));
            Assert.That((await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.RenderPdf)).Single().Payload["document_id"]!.GetValue<Guid>(),
                Is.EqualTo(document.Id));
        });
    }

    // php-rules 12.3, item 5: the PHP kept a rejected one pending and sent it again on every call.
    [Test]
    public async Task A_rejected_pending_type_04_ends_rejected_with_the_rejection_notice()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await PendingAsync(host, issuer);
        host.Dian.Reply(DianReplies.SendBillSync(false, "99", "Regla: FAD06, Rechazo: Valor del CUFE no está calculado correctamente"));

        await host.RunOutboxAsync();

        var notification = (await host.NotificationsAsync(issuer.CompanyId)).Single();
        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Rejected));
            Assert.That(notification.Type, Is.EqualTo(DianNotificationTypes.Rejected));
            Assert.That(notification.Body, Does.Contain(document.FullNumber).And.Contain("FAD06"));
            Assert.That(await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.RenderPdf), Is.Empty);
        });
    }

    [Test]
    public async Task A_type_04_the_dian_does_not_receive_stays_pending_and_the_outbox_retries_it()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await PendingAsync(host, issuer);
        host.Dian.Otherwise = () => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused");

        await host.RunOutboxAsync();

        var message = (await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendDocumentToDian)).Single();
        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.ContingencyPending));
            Assert.That((message.Status, message.Attempts), Is.EqualTo((OutboxStatus.Pending, 1)));
        });
    }

    [Test]
    public async Task When_the_outbox_gives_up_a_type_04_stays_pending_and_is_sent_again_30_minutes_later()
    {
        await using var host = await DianSendHost.StartAsync(new Dictionary<string, string?> { ["Outbox:MaxAttempts"] = "1" });
        var issuer = await host.CreateIssuerAsync();
        var document = await PendingAsync(host, issuer);
        host.Dian.Otherwise = () => throw new HttpRequestException(HttpRequestError.NameResolutionError, "No such host");

        await host.RunOutboxAsync();

        var messages = await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendDocumentToDian);
        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.ContingencyPending));
            Assert.That(messages.Select(message => message.Status), Is.EquivalentTo(new[] { OutboxStatus.Failed, OutboxStatus.Pending }));
            Assert.That(messages.Single(message => message.Status == OutboxStatus.Pending).NextAttemptAt,
                Is.EqualTo(host.Clock.UtcNow.AddMinutes(30)));
        });

        host.Clock.Advance(TimeSpan.FromMinutes(30));
        host.Dian.Otherwise = null;
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));
        await host.RunOutboxAsync();

        Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
    }

    [Test]
    public async Task After_the_48_hours_the_worker_stops_retrying_alone_and_notifies_the_company()
    {
        await using var host = await DianSendHost.StartAsync(new Dictionary<string, string?> { ["Outbox:MaxAttempts"] = "1" });
        var issuer = await host.CreateIssuerAsync();
        var document = await PendingAsync(host, issuer);
        host.Clock.Advance(TimeSpan.FromHours(48));
        host.Dian.Otherwise = () => throw new HttpRequestException(HttpRequestError.NameResolutionError, "No such host");

        await host.RunOutboxAsync();

        var messages = await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendDocumentToDian);
        var notices = await host.NotificationsAsync(issuer.CompanyId);
        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.ContingencyPending));
            Assert.That(messages.Select(message => message.Status), Is.EqualTo(new[] { OutboxStatus.Failed }));
            Assert.That(notices.Select(notice => notice.Type), Does.Contain(DianNotificationTypes.ContingencyPending));
            Assert.That(notices.Single(notice => notice.Type == DianNotificationTypes.ContingencyPending).Body,
                Does.Contain(document.FullNumber).And.Contain("send-pending"));
        });
    }

    // A SOAP Fault of the sender (certificate, software) fails a normal invoice; the type 04 must still reach the DIAN.
    [Test]
    public async Task A_soap_fault_keeps_the_type_04_pending_with_a_notice()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await PendingAsync(host, issuer);
        host.Dian.Otherwise = () => FakeDian.Response(DianReplies.ClientFault, HttpStatusCode.InternalServerError);

        await host.RunOutboxAsync();

        var stored = await host.DocumentAsync(document.Id);
        var notification = (await host.NotificationsAsync(issuer.CompanyId)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(DocumentStatus.ContingencyPending));
            Assert.That(stored.DianStatusDescription, Is.EqualTo("The creator of this fault did not specify a Reason."));
            Assert.That(notification.Type, Is.EqualTo(DianNotificationTypes.Refused));
            Assert.That(notification.Body, Does.Contain(document.FullNumber).And.Contain("sigue pendiente"));
        });
    }

    /// <summary>A stored invoice type 04 with its signed XML and the <c>SendDocumentToDian</c> message that sends it now.</summary>
    private static async Task<Document> PendingAsync(DianSendHost host, TestIssuer issuer)
    {
        var document = await host.CreateDocumentAsync(issuer, documentTypeId: DocumentFamilies.DianContingencyInvoiceType, status: DocumentStatus.ContingencyPending);
        await host.EnqueueSendAsync(document);
        return document;
    }
}
