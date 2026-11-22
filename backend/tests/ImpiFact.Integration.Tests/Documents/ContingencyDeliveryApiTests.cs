using System.Net;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Features.Documents.Delivery;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.Companies;
using ImpiFact.Integration.Tests.DianSend;
using Microsoft.EntityFrameworkCore;
using static ImpiFact.Integration.Tests.DocumentIssue.ContingencyInvoiceIssueApiTests;

// Same namespace as the issue tests so that DocumentIssueHost (a namespace-scoped SetUpFixture) starts the API for it.
namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// Contingency invoices from the API to a simulated DIAN (phase 7a, T5; S2 item 6: the type 04 cannot be forced in
/// habilitation, so it is proven with a golden and this test): the real API issues them and the real Worker, on the same
/// database, sends them with SendBillSync to a fake DIAN. A type 04 waits pending, with no DIAN call, until
/// <c>send-pending</c>; accepted it gets its AttachedDocument, its PDF and the plan consumption, rejected it ends
/// <c>Rejected</c>. A type 03 is sent right away like an invoice.
/// </summary>
[Category("Integration")]
public sealed class ContingencyDeliveryApiTests
{
    private static CompanyApiFactory Api => DocumentIssueHost.Api;

    [Test]
    public async Task A_type_04_waits_for_send_pending_and_is_then_accepted_with_its_attached_document_pdf_and_quota()
    {
        await using var worker = await WorkerAsync();
        using var issuer = await NewIssuerAsync();
        await AssignInvoicePlanAsync(issuer, quantity: 10, used: 0);
        using var issued = await PostAsync(issuer.Client, DianContingencyRoute, Body("invoice-contingency-4", "pending-transmission"));
        Assert.That(issued.StatusCode, Is.EqualTo(HttpStatusCode.OK), await issued.Content.ReadAsStringAsync());
        var document = await SingleDocumentAsync(issuer);

        await worker.RunOutboxAsync();

        Assert.That(worker.Dian.Requests, Is.Empty, "Nothing is sent before send-pending or the first automatic attempt.");
        Assert.That((await worker.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.ContingencyPending));

        worker.Dian.Reply(DianReplies.SendBillSync(true, "00"));
        using var sent = await issuer.Client.PostAsync(SendPendingRoute, null);
        Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), await sent.Content.ReadAsStringAsync());
        await worker.RunOutboxAsync();

        var stored = await worker.DocumentAsync(document.Id);
        var files = await worker.FilesAsync(document.Id);
        Assert.Multiple(async () =>
        {
            Assert.That(worker.Dian.Requests.Select(request => request.Operation), Is.EqualTo(new[] { "SendBillSync" }));
            Assert.That(stored.Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That(files.Select(file => file.Kind), Does.Contain(DocumentFileKind.AttachedDocument));
            Assert.That((await worker.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.RenderPdf)).Select(message => message.Payload["document_id"]!.GetValue<Guid>()),
                Is.EqualTo(new[] { document.Id }));
            Assert.That(await UsedAsync(issuer), Is.EqualTo(1L), "The type 04 counts in the invoice quota once accepted (php-rules 9.2, item 12).");
        });

        // The automatic attempt scheduled at issue finds it already decided and does nothing.
        worker.Clock.Advance(TimeSpan.FromMinutes(30));
        await worker.RunOutboxAsync();
        Assert.That(worker.Dian.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task A_rejected_type_04_ends_rejected_and_is_not_pending_any_more()
    {
        await using var worker = await WorkerAsync();
        using var issuer = await NewIssuerAsync();
        using var issued = await PostAsync(issuer.Client, DianContingencyRoute, Body("invoice-contingency-4", "pending-transmission"));
        Assert.That(issued.StatusCode, Is.EqualTo(HttpStatusCode.OK), await issued.Content.ReadAsStringAsync());
        var document = await SingleDocumentAsync(issuer);
        worker.Dian.Reply(DianReplies.SendBillSync(false, "99", "Regla: FAD06, Rechazo: Valor del CUFE no está calculado correctamente"));

        using var sent = await issuer.Client.PostAsync(SendPendingRoute, null);
        await worker.RunOutboxAsync();
        using var again = await issuer.Client.PostAsync(SendPendingRoute, null);

        using var json = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
        Assert.Multiple(async () =>
        {
            Assert.That((await worker.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Rejected));
            Assert.That((await worker.NotificationsAsync(issuer.CompanyId)).Select(row => row.Type), Is.EqualTo(new[] { DianNotificationTypes.Rejected }));
            Assert.That(json.RootElement.GetProperty("documents").GetArrayLength(), Is.Zero, "A rejected one is never sent again (php-rules 12.3, item 5).");
            Assert.That(await UsedAsync(issuer), Is.Null);
        });
    }

    [Test]
    public async Task A_type_03_is_issued_and_sent_right_away_like_an_invoice()
    {
        await using var worker = await WorkerAsync();
        using var issuer = await NewIssuerAsync();
        worker.Dian.Reply(DianReplies.SendBillSync(true, "00"));

        using var issued = await PostAsync(issuer.Client, PaperBackupRoute, Body("invoice-contingency", "paper-backup-reference"));
        Assert.That(issued.StatusCode, Is.EqualTo(HttpStatusCode.OK), await issued.Content.ReadAsStringAsync());
        await worker.RunOutboxAsync();

        var document = await SingleDocumentAsync(issuer);
        Assert.Multiple(() =>
        {
            Assert.That(worker.Dian.Requests.Select(request => request.Operation), Is.EqualTo(new[] { "SendBillSync" }));
            Assert.That(document.Status, Is.EqualTo(DocumentStatus.Accepted));
        });
    }

    /// <summary>
    /// A Worker on the API database, with its clock on the API time (the outbox compares due times with it), started after
    /// the messages of earlier tests were marked done.
    /// </summary>
    private static async Task<DianSendHost> WorkerAsync()
    {
        var worker = await DianSendHost.StartAsync(Api.SharedSettings, DocumentIssueHost.ConnectionString);
        worker.Clock.Advance(Api.Clock.UtcNow - worker.Clock.UtcNow);
        return worker;
    }

    private static async Task<long?> UsedAsync(Issuer issuer)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var companyPlanIds = await CompanyApiTests.Rows<CompanyPlan>(scope.Db, issuer.CompanyId).Select(row => row.Id).ToListAsync();
        var used = await scope.Db.Set<PlanUsage>().IgnoreQueryFilters().AsNoTracking()
            .Where(row => companyPlanIds.Contains(row.CompanyPlanId) && row.DocumentKind == DocumentKind.Invoice)
            .Select(row => (long?)row.Used).SingleOrDefaultAsync();
        return used == 0 ? null : used;
    }
}
