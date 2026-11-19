using System.Net;
using System.Net.Http;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Features.Documents.Delivery;
using ImpiFact.Dian.Soap;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Platform.Outbox;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// El Worker envía a la DIAN (la autoridad tributaria de Colombia) los documentos en <c>Queued</c> (Arquitectura
/// detallada, secciones 6.4 y 7, pasos 7 a 10; supuestos C2 y C4 de la fase 4), con el procesador real de la bandeja
/// de salida (outbox), la cadena de resiliencia real y una DIAN falsa.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class SendDocumentToDianTests
{
    [Test]
    public async Task An_accepted_invoice_ends_accepted_with_its_submission_and_files()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));

        await host.RunOutboxAsync();

        var stored = await host.DocumentAsync(document.Id);
        var submission = (await host.SubmissionsAsync(document.Id)).Single();
        var files = await host.FilesAsync(document.Id);
        var request = host.Dian.Requests.Single();
        var message = (await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendDocumentToDian)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That(stored.SentAt, Is.EqualTo(host.Clock.UtcNow));
            Assert.That(stored.AcceptedAt, Is.EqualTo(host.Clock.UtcNow));
            Assert.That(stored.DianStatusCode, Is.EqualTo("00"));
            Assert.That(stored.DianStatusDescription, Is.EqualTo("Procesado Correctamente."));
            Assert.That(message.Status, Is.EqualTo(OutboxStatus.Done));

            Assert.That(request.Url, Is.EqualTo(new Uri(DianSendHost.HabilitationUrl)));
            Assert.That(request.ContentType, Is.EqualTo("application/soap+xml; charset=utf-8"));
            Assert.That(request.Operation, Is.EqualTo("SendBillSync"));
            Assert.That(request.Parameter("fileName"), Is.EqualTo(document.FullNumber + ".xml"));
            Assert.That(request.Body, Does.Contain("wsse:BinarySecurityToken"));

            Assert.That(submission.Attempt, Is.EqualTo(1));
            Assert.That(submission.Operation, Is.EqualTo(DianOperation.SendBillSync));
            Assert.That(submission.TypeEnvironmentId, Is.EqualTo(2));
            Assert.That(submission.HttpStatus, Is.EqualTo(200));
            Assert.That(submission.IsValid, Is.True);
            Assert.That(submission.DianStatusCode, Is.EqualTo("00"));
            Assert.That(submission.ErrorMessages, Is.Null);
            Assert.That(submission.StartedAt, Is.EqualTo(host.Clock.UtcNow));
            Assert.That(submission.FinishedAt, Is.EqualTo(host.Clock.UtcNow));
            Assert.That(submission.DurationMs, Is.Not.Null);

            Assert.That(files.Select(file => (file.Kind, file.Revision)), Is.EquivalentTo(new[]
            {
                (DocumentFileKind.SignedXml, 1), (DocumentFileKind.DianResponse, 1), (DocumentFileKind.SentZip, 1),
                (DocumentFileKind.ApplicationResponse, 1), (DocumentFileKind.AttachedDocument, 1),
            }));
            Assert.That(submission.RequestFileId, Is.EqualTo(files.Single(file => file.Kind == DocumentFileKind.SentZip).Id));
            Assert.That(submission.ResponseFileId, Is.EqualTo(files.Single(file => file.Kind == DocumentFileKind.DianResponse).Id));
        });

        var sentZip = await host.ReadFileAsync(files.Single(file => file.Kind == DocumentFileKind.SentZip));
        Assert.Multiple(async () =>
        {
            Assert.That(Convert.ToBase64String(sentZip), Is.EqualTo(request.Parameter("contentFile")));
            Assert.That(DianSendHost.ZipEntryName(sentZip), Is.EqualTo(DianFileNames.XmlEntry(DianFileKind.Invoice, issuer.Nit, 2026, 1)));
            Assert.That(await host.ReadFileAsync(files.Single(file => file.Kind == DocumentFileKind.ApplicationResponse)),
                Is.EqualTo(System.Text.Encoding.UTF8.GetBytes(DianReplies.ApplicationResponseXml)));
        });
    }

    [Test]
    public async Task A_production_document_is_sent_with_SendBillSync_to_the_production_service_even_with_a_test_set()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer, typeEnvironmentId: 1, testSetId: "test-set-1");
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));

        await host.RunOutboxAsync();

        var request = host.Dian.Requests.Single();
        Assert.Multiple(async () =>
        {
            Assert.That(request.Url, Is.EqualTo(new Uri(DianSendHost.ProductionUrl)));
            Assert.That(request.Operation, Is.EqualTo("SendBillSync"));
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
        });
    }

    [Test]
    public async Task Notifications_make_it_accepted_with_warnings_and_are_kept_in_the_submission()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(true, "00", DianReplies.Notification));

        await host.RunOutboxAsync();

        var stored = await host.DocumentAsync(document.Id);
        var submission = (await host.SubmissionsAsync(document.Id)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(DocumentStatus.AcceptedWithWarnings));
            Assert.That(stored.AcceptedAt, Is.EqualTo(host.Clock.UtcNow));
            Assert.That(submission.ErrorMessages, Is.EqualTo(new[] { DianReplies.Notification }));
        });
    }

    [Test]
    public async Task Rejection_rules_make_it_rejected()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(false, "99", DianReplies.Rejection));

        await host.RunOutboxAsync();

        var stored = await host.DocumentAsync(document.Id);
        Assert.Multiple(async () =>
        {
            Assert.That(stored.Status, Is.EqualTo(DocumentStatus.Rejected));
            Assert.That(stored.DianStatusCode, Is.EqualTo("99"));
            Assert.That(stored.SentAt, Is.EqualTo(host.Clock.UtcNow));
            Assert.That(stored.AcceptedAt, Is.Null);
            Assert.That((await host.SubmissionsAsync(document.Id)).Single().IsValid, Is.False);
        });
    }

    [Test]
    public async Task Rule_90_is_reconciled_with_GetStatus_by_the_cufe()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(false, "99", DianReplies.Rule90));
        host.Dian.Reply(DianReplies.GetStatus(true, "00"));

        await host.RunOutboxAsync();

        var submissions = await host.SubmissionsAsync(document.Id);
        var requests = host.Dian.Requests;
        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That(requests.Select(request => request.Operation), Is.EqualTo(new[] { "SendBillSync", "GetStatus" }));
            Assert.That(requests[1].Parameter("trackId"), Is.EqualTo(document.Cufe));
            Assert.That(submissions.Select(row => (row.Attempt, row.Operation)),
                Is.EqualTo(new[] { (1, DianOperation.SendBillSync), (2, DianOperation.GetStatus) }));
            Assert.That(submissions[0].ErrorMessages, Is.EqualTo(new[] { DianReplies.Rule90 }));
            Assert.That(submissions[1].IsValid, Is.True);
            Assert.That(submissions[1].RequestFileId, Is.Null);
            Assert.That(submissions[1].ResponseFileId, Is.Not.Null);
        });
    }

    [Test]
    public async Task Rule_90_with_a_cufe_the_dian_does_not_know_is_rejected_because_another_document_used_the_number()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(false, "99", DianReplies.Rule90));
        host.Dian.Reply(DianReplies.GetStatus(false, "66", DianReplies.UnknownTrackId));

        await host.RunOutboxAsync();

        var stored = await host.DocumentAsync(document.Id);
        var submissions = await host.SubmissionsAsync(document.Id);
        var polls = await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus);
        var renders = await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.RenderPdf);
        var send = (await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendDocumentToDian)).Single();
        var notifications = await host.NotificationsAsync(issuer.CompanyId);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(DocumentStatus.Rejected));
            Assert.That(stored.DianStatusCode, Is.EqualTo("66"));
            Assert.That(stored.DianStatusDescription, Is.EqualTo(
                $"El número {document.FullNumber} ya fue usado en la DIAN por otro documento. " +
                "Revise la numeración de la resolución y emita la factura de nuevo."));
            Assert.That(stored.SentAt, Is.EqualTo(host.Clock.UtcNow));
            Assert.That(stored.AcceptedAt, Is.Null);
            Assert.That(host.Dian.Requests.Select(request => request.Operation), Is.EqualTo(new[] { "SendBillSync", "GetStatus" }));
            Assert.That(host.Dian.Requests[1].Parameter("trackId"), Is.EqualTo(document.Cufe));
            Assert.That(submissions.Select(row => (row.Operation, row.DianStatusCode)),
                Is.EqualTo(new[] { (DianOperation.SendBillSync, "99"), (DianOperation.GetStatus, "66") }));
            Assert.That(polls, Is.Empty);
            Assert.That(renders, Is.Empty);
            Assert.That(send.Status, Is.EqualTo(OutboxStatus.Done));
            Assert.That(notifications, Is.Empty);
        });
    }

    [Test]
    public async Task In_process_98_leaves_it_unconfirmed_and_GetStatus_later_accepts_it()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(false, "98"));

        await host.RunOutboxAsync();

        var unconfirmed = await host.DocumentAsync(document.Id);
        var poll = (await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(unconfirmed.Status, Is.EqualTo(DocumentStatus.Unconfirmed));
            Assert.That(unconfirmed.SentAt, Is.EqualTo(host.Clock.UtcNow));
            Assert.That(unconfirmed.DianStatusCode, Is.EqualTo("98"));
            Assert.That(poll.Status, Is.EqualTo(OutboxStatus.Pending));
            Assert.That(poll.NextAttemptAt, Is.EqualTo(host.Clock.UtcNow.AddMinutes(1)));
            Assert.That(poll.Payload["query"]!.GetValue<int>(), Is.EqualTo((int)DianStatusQuery.GetStatus));
            Assert.That(poll.Payload["document_id"]!.GetValue<Guid>(), Is.EqualTo(document.Id));
        });

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        host.Dian.Reply(DianReplies.GetStatus(true, "00"));
        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That(host.Dian.Requests[1].Parameter("trackId"), Is.EqualTo(document.Cufe));
            Assert.That((await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus)).Single().Status, Is.EqualTo(OutboxStatus.Done));
        });
    }

    [Test]
    public async Task A_timeout_after_sending_leaves_it_unconfirmed_with_a_status_poll()
    {
        await using var host = await DianSendHost.StartAsync(new Dictionary<string, string?>
        {
            ["Dian:TimeoutSeconds"] = "1", ["Resilience:Retries"] = "0", ["Resilience:TotalSeconds"] = "2",
        });
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Hang = true;

        await host.RunOutboxAsync();

        var stored = await host.DocumentAsync(document.Id);
        var submission = (await host.SubmissionsAsync(document.Id)).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(stored.Status, Is.EqualTo(DocumentStatus.Unconfirmed));
            Assert.That(submission.HttpStatus, Is.Null);
            Assert.That(submission.FinishedAt, Is.Not.Null);
            Assert.That((await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus)).Single().Status, Is.EqualTo(OutboxStatus.Pending));
        });
    }

    [Test]
    public async Task A_test_set_document_is_sent_with_SendTestSetAsync_and_its_zip_key_is_polled_until_accepted()
    {
        const string zipKey = "3f6e1d52-8a3b-4c1e-9b7a-2d4c5e6f7a8b";
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer, testSetId: "c3a1f0e2-test-set");
        host.Dian.Reply(DianReplies.SendTestSetAsync(zipKey));

        await host.RunOutboxAsync();

        var sent = await host.DocumentAsync(document.Id);
        var poll = (await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus)).Single();
        var first = (await host.SubmissionsAsync(document.Id)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(host.Dian.Requests[0].Operation, Is.EqualTo("SendTestSetAsync"));
            Assert.That(host.Dian.Requests[0].Parameter("testSetId"), Is.EqualTo("c3a1f0e2-test-set"));
            Assert.That(sent.Status, Is.EqualTo(DocumentStatus.Sent));
            Assert.That(sent.TrackId, Is.EqualTo(zipKey));
            Assert.That(first.Operation, Is.EqualTo(DianOperation.SendTestSetAsync));
            Assert.That(first.ZipKey, Is.EqualTo(zipKey));
            Assert.That(poll.NextAttemptAt, Is.EqualTo(host.Clock.UtcNow.AddSeconds(15)));
            Assert.That(poll.Payload["query"]!.GetValue<int>(), Is.EqualTo((int)DianStatusQuery.GetStatusZip));
        });

        host.Clock.Advance(TimeSpan.FromSeconds(15));
        host.Dian.Reply(DianReplies.GetStatusZip(false, "98"));
        await host.RunOutboxAsync();

        var waiting = await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus);
        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Sent));
            Assert.That(waiting, Has.Count.EqualTo(2));
            Assert.That(waiting[1].NextAttemptAt, Is.EqualTo(host.Clock.UtcNow.AddMinutes(1)));
        });

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        host.Dian.Reply(DianReplies.GetStatusZip(true, "00"));
        await host.RunOutboxAsync();

        var submissions = await host.SubmissionsAsync(document.Id);
        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That(host.Dian.Requests.Select(request => request.Operation), Is.EqualTo(new[] { "SendTestSetAsync", "GetStatusZip", "GetStatusZip" }));
            Assert.That(host.Dian.Requests[2].Parameter("trackId"), Is.EqualTo(zipKey));
            Assert.That(submissions.Select(row => row.Operation),
                Is.EqualTo(new[] { DianOperation.SendTestSetAsync, DianOperation.GetStatusZip, DianOperation.GetStatusZip }));
            Assert.That(submissions[2].TrackId, Is.EqualTo(zipKey));
        });
    }

    [Test]
    public async Task A_soap_fault_with_http_500_is_not_retried_and_fails_the_document_with_a_notification()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Otherwise = () => FakeDian.Response(DianReplies.ClientFault, HttpStatusCode.InternalServerError);

        await host.RunOutboxAsync();

        var stored = await host.DocumentAsync(document.Id);
        var submission = (await host.SubmissionsAsync(document.Id)).Single();
        var notification = (await host.NotificationsAsync(issuer.CompanyId)).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(host.Dian.Requests, Has.Count.EqualTo(1));
            Assert.That(stored.Status, Is.EqualTo(DocumentStatus.Failed));
            Assert.That(stored.DianStatusDescription, Is.EqualTo("The creator of this fault did not specify a Reason."));
            Assert.That(submission.HttpStatus, Is.EqualTo(500));
            Assert.That(submission.ResponseFileId, Is.Not.Null);
            Assert.That(notification.Type, Is.EqualTo(DianNotificationTypes.Refused));
            Assert.That(notification.Body, Does.Contain(document.FullNumber));
            Assert.That((await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendDocumentToDian)).Single().Status,
                Is.EqualTo(OutboxStatus.Done));
        });
    }

    [Test]
    public async Task A_service_unavailable_page_is_retried_by_the_http_client()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.ServiceUnavailablePage, HttpStatusCode.ServiceUnavailable);
        host.Dian.Reply(DianReplies.ServiceUnavailablePage, HttpStatusCode.ServiceUnavailable);
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));

        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(host.Dian.Requests, Has.Count.EqualTo(3));
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That((await host.SubmissionsAsync(document.Id)).Single().HttpStatus, Is.EqualTo(200));
        });
    }

    [Test]
    public async Task A_connection_failure_keeps_it_queued_and_the_outbox_retries_the_message()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Otherwise = () => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused");

        await host.RunOutboxAsync();

        var message = (await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendDocumentToDian)).Single();
        var submission = (await host.SubmissionsAsync(document.Id)).Single();
        Assert.Multiple(async () =>
        {
            // El primer intento y los tres reintentos del cliente HTTP.
            Assert.That(host.Dian.Requests, Has.Count.EqualTo(4));
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Queued));
            Assert.That(message.Status, Is.EqualTo(OutboxStatus.Pending));
            Assert.That(message.Attempts, Is.EqualTo(1));
            Assert.That(message.NextAttemptAt, Is.EqualTo(host.Clock.UtcNow.AddSeconds(10)));
            Assert.That(message.LastError, Does.Contain(nameof(DianNotDeliveredException)));
            Assert.That(submission.HttpStatus, Is.Null);
            Assert.That(submission.FinishedAt, Is.EqualTo(host.Clock.UtcNow));
        });

        host.Clock.Advance(TimeSpan.FromSeconds(10));
        host.Dian.Otherwise = null;
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));
        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That((await host.SubmissionsAsync(document.Id)).Select(row => row.Attempt), Is.EqualTo(new[] { 1, 2 }));
        });
    }

    [Test]
    public async Task When_the_outbox_gives_up_the_queued_document_ends_failed()
    {
        await using var host = await DianSendHost.StartAsync(new Dictionary<string, string?> { ["Outbox:MaxAttempts"] = "1" });
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Otherwise = () => throw new HttpRequestException(HttpRequestError.NameResolutionError, "No such host");

        await host.RunOutboxAsync();

        var message = (await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendDocumentToDian)).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(message.Status, Is.EqualTo(OutboxStatus.Failed));
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Failed));
            Assert.That((await host.NotificationsAsync(issuer.CompanyId)).Select(row => row.Type), Is.EqualTo(new[] { OutboxNotifications.FailedType }));
        });
    }

    [Test]
    public async Task After_five_failed_attempts_the_circuit_opens_and_the_next_send_is_not_attempted()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var first = await host.CreateDocumentAsync(issuer);
        host.Dian.Otherwise = () => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused");
        await host.RunOutboxAsync();
        var second = await host.CreateDocumentAsync(issuer);

        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            // Cuatro intentos del primer envío y uno del segundo abren el circuito; los reintentos restantes no salen.
            Assert.That(host.Dian.Requests, Has.Count.EqualTo(5));
            Assert.That((await host.DocumentAsync(first.Id)).Status, Is.EqualTo(DocumentStatus.Queued));
            Assert.That((await host.DocumentAsync(second.Id)).Status, Is.EqualTo(DocumentStatus.Queued));
            Assert.That((await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendDocumentToDian)).Last().LastError,
                Does.Contain("circuit breaker"));
        });
    }

    [Test]
    public async Task A_timeout_masked_by_unavailable_retries_ends_unconfirmed_and_the_status_poll_accepts_it()
    {
        await using var host = await DianSendHost.StartAsync(new Dictionary<string, string?>
        {
            ["Dian:TimeoutSeconds"] = "1", ["Resilience:TotalSeconds"] = "10", ["Outbox:MaxAttempts"] = "2",
        });
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        // El primer intento llega y se queda sin respuesta; después la DIAN solo devuelve 503 a los envíos, pero ya tiene
        // el documento y GetStatus lo da por aceptado.
        host.Dian.HangOnce();
        host.Dian.Route = request => request.Operation == "GetStatus"
            ? FakeDian.Response(DianReplies.GetStatus(true, "00"))
            : FakeDian.Response(DianReplies.ServiceUnavailablePage, HttpStatusCode.ServiceUnavailable);

        await host.RunOutboxAsync();
        host.Clock.Advance(TimeSpan.FromSeconds(10));
        await host.RunOutboxAsync();
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.RunOutboxAsync();

        var stored = await host.DocumentAsync(document.Id);
        Assert.Multiple(async () =>
        {
            Assert.That(stored.Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That(host.Dian.Requests.Count(request => request.Operation == "SendBillSync"), Is.EqualTo(4));
            Assert.That(host.Dian.Requests.Last().Operation, Is.EqualTo("GetStatus"));
            Assert.That((await host.NotificationsAsync(issuer.CompanyId)), Is.Empty);
        });
    }

    [Test]
    public async Task Every_retry_of_a_call_carries_a_freshly_built_envelope()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.OnRequest = _ => host.Clock.Advance(TimeSpan.FromSeconds(1));
        host.Dian.Reply(DianReplies.ServiceUnavailablePage, HttpStatusCode.ServiceUnavailable);
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));

        await host.RunOutboxAsync();

        var requests = host.Dian.Requests;
        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[0].Created, Is.Not.Empty);
            Assert.That(requests[1].Created, Is.Not.EqualTo(requests[0].Created));
            Assert.That(requests[1].Parameter("contentFile"), Is.EqualTo(requests[0].Parameter("contentFile")));
        });
    }

    [Test]
    public async Task A_sender_fault_after_an_attempt_that_got_no_answer_is_not_a_refusal()
    {
        await using var host = await DianSendHost.StartAsync(new Dictionary<string, string?>
        {
            ["Dian:TimeoutSeconds"] = "1", ["Resilience:TotalSeconds"] = "10",
        });
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        // Por ejemplo, la DIAN toma el reintento como una repetición del primer sobre, que sí recibió.
        host.Dian.HangOnce();
        host.Dian.Reply(DianReplies.ClientFault, HttpStatusCode.InternalServerError);

        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Unconfirmed));
            Assert.That((await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus)).Single().Status,
                Is.EqualTo(OutboxStatus.Pending));
            Assert.That(await host.NotificationsAsync(issuer.CompanyId), Is.Empty);
        });
    }

    [Test]
    public async Task A_storage_failure_after_the_dian_answered_ends_unconfirmed_and_the_status_poll_accepts_it()
    {
        await using var host = await DianSendHost.StartAsync(new Dictionary<string, string?> { ["Outbox:MaxAttempts"] = "2" });
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        // El almacenamiento se cae justo cuando la DIAN recibe el documento: su respuesta no se puede guardar.
        host.Dian.OnRequest = _ => host.Storage.Failing = true;
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));

        await host.RunOutboxAsync();
        host.Clock.Advance(TimeSpan.FromSeconds(10));
        await host.RunOutboxAsync();

        var unconfirmed = await host.DocumentAsync(document.Id);
        var poll = (await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus)).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(unconfirmed.Status, Is.EqualTo(DocumentStatus.Unconfirmed));
            Assert.That(unconfirmed.SentAt, Is.EqualTo(host.Clock.UtcNow.AddSeconds(-10)));
            Assert.That(poll.Status, Is.EqualTo(OutboxStatus.Pending));
            Assert.That(poll.Payload["query"]!.GetValue<int>(), Is.EqualTo((int)DianStatusQuery.GetStatus));
            Assert.That((await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendDocumentToDian)).Single().Status,
                Is.EqualTo(OutboxStatus.Failed));
        });

        host.Storage.Failing = false;
        host.Dian.OnRequest = null;
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        host.Dian.Reply(DianReplies.GetStatus(true, "00"));
        await host.RunOutboxAsync();

        Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
    }

    [Test]
    public async Task A_sender_fault_on_a_resend_after_an_unrecorded_answer_is_not_a_refusal()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.OnRequest = _ => host.Storage.Failing = true;
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));
        await host.RunOutboxAsync();

        host.Storage.Failing = false;
        host.Dian.OnRequest = null;
        host.Clock.Advance(TimeSpan.FromSeconds(10));
        host.Dian.Reply(DianReplies.ClientFault, HttpStatusCode.InternalServerError);
        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Unconfirmed));
            Assert.That((await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.PollDianStatus)).Single().Status,
                Is.EqualTo(OutboxStatus.Pending));
            Assert.That((await host.NotificationsAsync(issuer.CompanyId)).Select(row => row.Type),
                Has.None.EqualTo(DianNotificationTypes.Refused));
        });
    }

    [Test]
    public async Task A_repeated_message_for_a_document_already_sent_does_nothing()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer, status: DocumentStatus.Accepted);
        await host.EnqueueSendAsync(document);

        await host.RunOutboxAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(host.Dian.Requests, Is.Empty);
            Assert.That(await host.SubmissionsAsync(document.Id), Is.Empty);
            Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
            Assert.That((await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendDocumentToDian)).Single().Status, Is.EqualTo(OutboxStatus.Done));
        });
    }

    [Test]
    public async Task The_file_name_counter_increments_per_company_kind_and_year()
    {
        // De a un mensaje por tanda: el primer envío del año crea el consecutivo, y dos a la vez chocarían (y uno se
        // reintentaría después).
        await using var host = await DianSendHost.StartAsync(new Dictionary<string, string?> { ["Outbox:BatchSize"] = "1" });
        var issuer = await host.CreateIssuerAsync();
        var other = await host.CreateIssuerAsync();
        host.Dian.Otherwise = () => FakeDian.Response(DianReplies.SendBillSync(true, "00"));
        await host.CreateDocumentAsync(issuer);
        await host.CreateDocumentAsync(issuer);
        await host.CreateDocumentAsync(issuer, documentTypeId: 4);
        await host.CreateDocumentAsync(other);

        while (await host.RunOutboxAsync() > 0)
        {
        }

        var entries = host.Dian.Requests.Select(request => DianSendHost.ZipEntryName(Convert.FromBase64String(request.Parameter("contentFile")!)));
        Assert.Multiple(async () =>
        {
            Assert.That(entries, Is.EquivalentTo(new[]
            {
                DianFileNames.XmlEntry(DianFileKind.Invoice, issuer.Nit, 2026, 1),
                DianFileNames.XmlEntry(DianFileKind.Invoice, issuer.Nit, 2026, 2),
                DianFileNames.XmlEntry(DianFileKind.CreditNote, issuer.Nit, 2026, 1),
                DianFileNames.XmlEntry(DianFileKind.Invoice, other.Nit, 2026, 1),
            }));
            Assert.That(await host.NextFileCounterAsync(issuer.CompanyId, NumberingScope.ForDianFile("fv", 2026)), Is.EqualTo(3));
            Assert.That(await host.NextFileCounterAsync(issuer.CompanyId, NumberingScope.ForDianFile("nc", 2026)), Is.EqualTo(2));
            Assert.That(await host.NextFileCounterAsync(other.CompanyId, NumberingScope.ForDianFile("fv", 2026)), Is.EqualTo(2));
        });
    }
}
