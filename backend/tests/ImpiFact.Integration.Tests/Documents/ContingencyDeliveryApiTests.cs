using System.Net;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Features.Documents.Delivery;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
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

    // End-of-7a verification, M1: send-pending added a second SendDocumentToDian message on top of the one scheduled at
    // issue, both ran at once and both called SendBillSync. Now send-pending brings the waiting message forward instead.
    [Test]
    public async Task Two_send_pending_calls_make_a_single_soap_call()
    {
        await using var worker = await WorkerAsync();
        using var issuer = await NewIssuerAsync();
        await SendFirstOfTheYearAsync(worker, issuer);
        var before = worker.Dian.Requests.Count;
        await IssueType04Async(issuer, numberOffset: 1);
        worker.Dian.Reply(DianReplies.SendBillSync(true, "00"));
        worker.Dian.Reply(DianReplies.SendBillSync(true, "00"));

        using (var first = await issuer.Client.PostAsync(SendPendingRoute, null))
        using (var second = await issuer.Client.PostAsync(SendPendingRoute, null))
        {
            Assert.That((first.StatusCode, second.StatusCode), Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK)));
        }

        await worker.RunOutboxAsync();
        await worker.RunOutboxAsync();

        Assert.That(worker.Dian.Requests.Count(request => request.Operation == "SendBillSync") - before, Is.EqualTo(1));
    }

    // M1, inactive company: every held message was scheduled again, so the duplicates waited together and fired together
    // once the company was reactivated.
    [Test]
    public async Task A_type_04_held_while_the_company_is_inactive_is_sent_once_after_the_reactivation()
    {
        await using var worker = await WorkerAsync();
        using var issuer = await NewIssuerAsync();
        await SendFirstOfTheYearAsync(worker, issuer);
        var before = worker.Dian.Requests.Count;
        var document = await IssueType04Async(issuer, numberOffset: 1);
        using (await issuer.Client.PostAsync(SendPendingRoute, null))
        {
        }

        using var platform = await Api.ClientWithNewKeyAsync(null, ApiScopes.Platform);
        using (var deactivated = await platform.PostAsync($"/api/v1/platform/companies/{issuer.Nit}/deactivate", null))
        {
            Assert.That(deactivated.StatusCode, Is.EqualTo(HttpStatusCode.OK), await deactivated.Content.ReadAsStringAsync());
        }

        for (var round = 0; round < 3; round++)
        {
            await worker.RunOutboxAsync();
            worker.Clock.Advance(TimeSpan.FromMinutes(31));
        }

        Assert.That(worker.Dian.Requests, Has.Count.EqualTo(before), "Nothing is sent while the company is inactive.");
        using (var reactivated = await platform.PostAsync($"/api/v1/platform/companies/{issuer.Nit}/reactivate", null))
        {
            Assert.That(reactivated.StatusCode, Is.EqualTo(HttpStatusCode.OK), await reactivated.Content.ReadAsStringAsync());
        }

        worker.Dian.Reply(DianReplies.SendBillSync(true, "00"));
        worker.Dian.Reply(DianReplies.SendBillSync(true, "00"));
        worker.Clock.Advance(TimeSpan.FromMinutes(31));
        await worker.RunOutboxAsync();
        worker.Clock.Advance(TimeSpan.FromMinutes(1));
        await worker.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(worker.Dian.Requests.Skip(before).Select(request => request.Operation), Is.EqualTo(new[] { "SendBillSync" }));
            Assert.That((await worker.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
        });
    }

    // M1, the claim: two messages of one document that run at the same time (for example, rows queued before this fix)
    // make one SOAP call; the second one fails its claim before calling the DIAN and ends without retrying or failing.
    [Test]
    public async Task Two_messages_of_one_type_04_running_at_once_make_a_single_soap_call()
    {
        await using var worker = await WorkerAsync();
        using var issuer = await NewIssuerAsync();
        await SendFirstOfTheYearAsync(worker, issuer);
        var before = worker.Dian.Requests.Count;
        var document = await IssueType04Async(issuer, numberOffset: 1);
        using (await issuer.Client.PostAsync(SendPendingRoute, null))
        {
        }

        await worker.EnqueueSendAsync(document);
        worker.Dian.Reply(DianReplies.SendBillSync(true, "00"));
        worker.Dian.Reply(DianReplies.SendBillSync(true, "00"));

        await worker.RunOutboxAsync();
        worker.Clock.Advance(TimeSpan.FromMinutes(1));
        await worker.RunOutboxAsync();

        var messages = await worker.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendDocumentToDian);
        Assert.Multiple(async () =>
        {
            Assert.That(worker.Dian.Requests.Skip(before).Select(request => request.Operation), Is.EqualTo(new[] { "SendBillSync" }));
            Assert.That((await worker.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That((await worker.SubmissionsAsync(document.Id)).Select(submission => submission.Attempt), Is.EqualTo(new[] { 1 }));
            Assert.That(messages.Select(message => message.Status), Is.All.EqualTo(OutboxStatus.Done));
        });
    }

    /// <summary>
    /// Issues a type 04 and sends it, so the fv file counter of the year exists: later sends of the test do not race to
    /// create it (the loser of that race retries 10 s later, which would hide a second SOAP call).
    /// </summary>
    private static async Task SendFirstOfTheYearAsync(DianSendHost worker, Issuer issuer)
    {
        var first = await IssueType04Async(issuer, numberOffset: 0);
        worker.Dian.Reply(DianReplies.SendBillSync(true, "00"));
        using (var sent = await issuer.Client.PostAsync(SendPendingRoute, null))
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), await sent.Content.ReadAsStringAsync());
        }

        await worker.RunOutboxAsync();
        Assert.That((await worker.DocumentAsync(first.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
    }

    /// <summary>Issues the golden type 04 with its number moved by <paramref name="numberOffset"/>; returns the stored document.</summary>
    private static async Task<Document> IssueType04Async(Issuer issuer, long numberOffset)
    {
        using (var issued = await PostAsync(issuer.Client, DianContingencyRoute,
            Edited("invoice-contingency-4", "pending-transmission", node => node["number"] = node["number"]!.GetValue<long>() + numberOffset)))
        {
            Assert.That(issued.StatusCode, Is.EqualTo(HttpStatusCode.OK), await issued.Content.ReadAsStringAsync());
        }

        await using var scope = Api.OpenScope(null, null, ActorType.System);
        return await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).OrderByDescending(document => document.Number).FirstAsync();
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
