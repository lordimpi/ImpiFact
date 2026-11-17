using System.Net.Http;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Features.Documents.Delivery;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Platform.Outbox;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// La consulta del estado en la DIAN de los documentos enviados sin decisión: «cada 1 minuto durante ~10 minutos y luego
/// cada 15 minutos hasta 24 h; pasado ese plazo pasa a <c>Failed</c> con alerta» (Arquitectura detallada, sección 7).
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class PollDianStatusTests
{
    [Test]
    public async Task After_ten_minutes_an_undecided_document_is_polled_again_in_15_minutes()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer, status: DocumentStatus.Unconfirmed, sentAt: host.Clock.UtcNow.AddHours(-1));
        await host.EnqueuePollAsync(document, DianStatusQuery.GetStatus);
        host.Dian.Reply(DianReplies.GetStatus(false, "98"));

        await host.RunOutboxAsync();

        var polls = await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus);
        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Unconfirmed));
            Assert.That(polls.Select(poll => poll.Status), Is.EqualTo(new[] { OutboxStatus.Done, OutboxStatus.Pending }));
            Assert.That(polls[1].NextAttemptAt, Is.EqualTo(host.Clock.UtcNow.AddMinutes(15)));
            Assert.That((await host.SubmissionsAsync(document.Id)).Single().Operation, Is.EqualTo(DianOperation.GetStatus));
        });
    }

    [Test]
    public async Task A_network_failure_while_polling_counts_as_a_poll_without_decision()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer, status: DocumentStatus.Unconfirmed, sentAt: host.Clock.UtcNow.AddMinutes(-2));
        await host.EnqueuePollAsync(document, DianStatusQuery.GetStatus);
        host.Dian.Otherwise = () => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused");

        await host.RunOutboxAsync();

        var polls = await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus);
        Assert.Multiple(() =>
        {
            Assert.That(polls.Select(poll => poll.Status), Is.EqualTo(new[] { OutboxStatus.Done, OutboxStatus.Pending }));
            Assert.That(polls[0].Attempts, Is.EqualTo(1));
            Assert.That(polls[1].NextAttemptAt, Is.EqualTo(host.Clock.UtcNow.AddMinutes(1)));
        });
    }

    [Test]
    public async Task Twenty_four_hours_after_the_send_without_decision_the_document_fails_with_a_notification()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer, status: DocumentStatus.Unconfirmed, sentAt: host.Clock.UtcNow.AddHours(-24));
        await host.EnqueuePollAsync(document, DianStatusQuery.GetStatus);
        host.Dian.Reply(DianReplies.GetStatus(false, "98"));

        await host.RunOutboxAsync();

        var notification = (await host.NotificationsAsync(issuer.CompanyId)).Single();
        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Failed));
            Assert.That((await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus)).Single().Status, Is.EqualTo(OutboxStatus.Done));
            Assert.That(notification.Type, Is.EqualTo(DianNotificationTypes.Unconfirmed));
            Assert.That(notification.Title, Is.EqualTo("Un documento quedó sin confirmar en la DIAN"));
            Assert.That(notification.Body, Does.Contain(document.FullNumber).And.Contain("24 horas"));
        });
    }

    [Test]
    public async Task A_sent_test_set_document_still_waiting_after_24_hours_fails_too()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(
            issuer, status: DocumentStatus.Sent, sentAt: host.Clock.UtcNow.AddHours(-25), trackId: "zip-key-1");
        await host.EnqueuePollAsync(document, DianStatusQuery.GetStatusZip);
        host.Dian.Reply(DianReplies.GetStatusZip(false, "98"));

        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(host.Dian.Requests.Single().Parameter("trackId"), Is.EqualTo("zip-key-1"));
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Failed));
            Assert.That((await host.NotificationsAsync(issuer.CompanyId)).Single().Type, Is.EqualTo(DianNotificationTypes.Unconfirmed));
        });
    }

    [Test]
    public async Task Rule_90_while_polling_the_zip_key_is_reconciled_with_GetStatus()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer, status: DocumentStatus.Sent, sentAt: host.Clock.UtcNow, trackId: "zip-key-2");
        await host.EnqueuePollAsync(document, DianStatusQuery.GetStatusZip);
        host.Dian.Reply(DianReplies.GetStatusZip(false, "99", DianReplies.Rule90));
        host.Dian.Reply(DianReplies.GetStatus(false, "99", DianReplies.Rejection));

        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(host.Dian.Requests.Select(request => request.Operation), Is.EqualTo(new[] { "GetStatusZip", "GetStatus" }));
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Rejected));
        });
    }

    [Test]
    public async Task Without_an_active_certificate_an_unconfirmed_document_keeps_being_polled()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(false, "98"));
        await host.RunOutboxAsync();
        await host.DeactivateCertificatesAsync(issuer);

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.RunOutboxAsync();

        var polls = await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus);
        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Unconfirmed));
            Assert.That(host.Dian.Requests, Has.Count.EqualTo(1));
            Assert.That(polls.Select(poll => poll.Status), Is.EqualTo(new[] { OutboxStatus.Done, OutboxStatus.Pending }));
            Assert.That(polls[1].NextAttemptAt, Is.EqualTo(host.Clock.UtcNow.AddMinutes(1)));
        });
    }

    [Test]
    public async Task Without_an_active_certificate_after_24_hours_the_document_fails_with_a_notification()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(false, "98"));
        await host.RunOutboxAsync();
        await host.DeactivateCertificatesAsync(issuer);

        host.Clock.Advance(TimeSpan.FromHours(24));
        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Failed));
            Assert.That((await host.NotificationsAsync(issuer.CompanyId)).Select(row => row.Type),
                Is.EqualTo(new[] { DianNotificationTypes.Unconfirmed }));
            Assert.That((await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus)).Select(poll => poll.Status),
                Is.EqualTo(new[] { OutboxStatus.Done }));
        });
    }

    [Test]
    public async Task When_the_outbox_gives_up_a_poll_the_document_is_polled_again()
    {
        await using var host = await DianSendHost.StartAsync(new Dictionary<string, string?> { ["Outbox:MaxAttempts"] = "1" });
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer, status: DocumentStatus.Unconfirmed, sentAt: host.Clock.UtcNow.AddMinutes(-2));
        await host.EnqueuePollAsync(document, DianStatusQuery.GetStatus);
        host.RemoveStoredCertificates();

        await host.RunOutboxAsync();

        var polls = await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus);
        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Unconfirmed));
            Assert.That(polls.Select(poll => poll.Status), Is.EqualTo(new[] { OutboxStatus.Failed, OutboxStatus.Pending }));
            Assert.That(polls[1].NextAttemptAt, Is.EqualTo(host.Clock.UtcNow.AddMinutes(1)));
        });
    }

    [Test]
    public async Task When_the_outbox_gives_up_a_poll_after_24_hours_the_document_fails_with_a_notification()
    {
        await using var host = await DianSendHost.StartAsync(new Dictionary<string, string?> { ["Outbox:MaxAttempts"] = "1" });
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(
            issuer, status: DocumentStatus.Sent, sentAt: host.Clock.UtcNow.AddHours(-25), trackId: "zip-key-3");
        await host.EnqueuePollAsync(document, DianStatusQuery.GetStatusZip);
        host.RemoveStoredCertificates();

        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Failed));
            Assert.That((await host.NotificationsAsync(issuer.CompanyId)).Select(row => row.Type),
                Is.EquivalentTo(new[] { OutboxNotifications.FailedType, DianNotificationTypes.Unconfirmed }));
            Assert.That((await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus)).Single().Status,
                Is.EqualTo(OutboxStatus.Failed));
        });
    }

    [Test]
    public async Task A_cufe_the_dian_does_not_know_yet_is_not_a_rejection_and_is_polled_again()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer, status: DocumentStatus.Unconfirmed, sentAt: host.Clock.UtcNow.AddMinutes(-2));
        await host.EnqueuePollAsync(document, DianStatusQuery.GetStatus);
        host.Dian.Reply(DianReplies.GetStatus(false, "66", "TrackId no existe en los registros de la DIAN."));

        await host.RunOutboxAsync();

        var polls = await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus);
        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Unconfirmed));
            Assert.That(polls.Select(poll => poll.Status), Is.EqualTo(new[] { OutboxStatus.Done, OutboxStatus.Pending }));
        });
    }

    [Test]
    public async Task A_poll_for_a_document_already_decided_does_nothing()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer, status: DocumentStatus.Accepted, sentAt: host.Clock.UtcNow);
        await host.EnqueuePollAsync(document, DianStatusQuery.GetStatus);

        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(host.Dian.Requests, Is.Empty);
            Assert.That((await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus)).Single().Status, Is.EqualTo(OutboxStatus.Done));
        });
    }
}
