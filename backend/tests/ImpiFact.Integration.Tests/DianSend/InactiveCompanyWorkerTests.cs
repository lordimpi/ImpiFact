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
/// The Worker and a company the platform deactivated (phase 7a, T9b1): nothing new reaches the DIAN while it is inactive
/// (the send is held and looked at again every 30 minutes, without failing the document), and once it is reactivated the
/// held work goes out by itself. The status polls of what the DIAN already received keep running.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class InactiveCompanyWorkerTests
{
    /// <summary>InactiveCompanyHold.RetryInterval: the 30 minutes of the contingency monitoring.</summary>
    private static readonly TimeSpan HoldInterval = TimeSpan.FromMinutes(30);

    [Test]
    public async Task A_queued_document_and_a_pending_type_04_are_held_while_inactive_and_sent_once_reactivated()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var queued = await host.CreateDocumentAsync(issuer);
        var contingency = await host.CreateDocumentAsync(issuer, documentTypeId: DocumentFamilies.DianContingencyInvoiceType, status: DocumentStatus.ContingencyPending);
        await host.EnqueueSendAsync(contingency);
        await SetActiveAsync(host, issuer, false);

        await host.RunOutboxAsync();

        var held = await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendDocumentToDian);
        Assert.Multiple(async () =>
        {
            Assert.That(host.Dian.Requests, Is.Empty, "Nothing is sent to the DIAN while the company is inactive.");
            Assert.That((await host.DocumentAsync(queued.Id)).Status, Is.EqualTo(DocumentStatus.Queued));
            Assert.That((await host.DocumentAsync(contingency.Id)).Status, Is.EqualTo(DocumentStatus.ContingencyPending));
            Assert.That(await host.SubmissionsAsync(queued.Id), Is.Empty, "No attempt is recorded.");
            Assert.That(await host.SubmissionsAsync(contingency.Id), Is.Empty);
            Assert.That(held.Select(message => message.Status), Is.EqualTo(new[] { OutboxStatus.Done, OutboxStatus.Done, OutboxStatus.Pending, OutboxStatus.Pending }));
            Assert.That(held.Where(message => message.Status == OutboxStatus.Pending).Select(message => message.NextAttemptAt),
                Is.All.EqualTo(host.Clock.UtcNow + HoldInterval));
            Assert.That(await host.NotificationsAsync(issuer.CompanyId), Is.Empty, "Holding is not a failure: no notice.");
        });

        await SetActiveAsync(host, issuer, true);
        host.Clock.Advance(HoldInterval);
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));

        await host.RunOutboxAsync();
        // Both sends open the year's file counter at once: one of them may hit the unique key and retry 10 s later.
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(host.Dian.Requests.Select(request => request.Operation), Is.EqualTo(new[] { "SendBillSync", "SendBillSync" }));
            Assert.That((await host.DocumentAsync(queued.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That((await host.DocumentAsync(contingency.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
        });
    }

    [Test]
    public async Task The_status_poll_of_a_document_the_dian_already_received_keeps_running_while_inactive()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer, status: DocumentStatus.Unconfirmed, sentAt: host.Clock.UtcNow.AddMinutes(-2));
        await host.EnqueuePollAsync(document, DianStatusQuery.GetStatus);
        await SetActiveAsync(host, issuer, false);
        host.Dian.Reply(DianReplies.GetStatus(true, "00"));

        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(host.Dian.Requests.Select(request => request.Operation), Is.EqualTo(new[] { "GetStatus" }));
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
        });
    }

    private static async Task SetActiveAsync(DianSendHost host, TestIssuer issuer, bool active)
    {
        await using var scope = host.OpenScope();
        await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().Set<Company>()
            .Where(company => company.Id == issuer.CompanyId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(company => company.IsActive, active));
    }
}
