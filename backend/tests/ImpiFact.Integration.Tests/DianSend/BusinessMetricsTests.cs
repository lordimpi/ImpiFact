using System.Diagnostics;
using System.Net.Http;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Observability;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.Observability;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// Métricas de negocio del Worker (Arquitectura detallada, sección 12): documentos emitidos, aceptados y rechazados
/// por tipo, duración y resultado de las llamadas a la DIAN, mensajes de la bandeja de salida procesados, fallidos y
/// su espera. Ninguna etiqueta lleva el NIT ni el id de la empresa o del documento.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class BusinessMetricsTests
{
    [Test]
    public async Task An_accepted_invoice_counts_as_issued_and_accepted_with_its_dian_call_and_outbox_message()
    {
        await using var host = await DianSendHost.StartAsync();
        using var metrics = new MetricRecorder(host.Services);
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));

        await host.RunOutboxAsync();

        Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
        var call = metrics.Of("impifact.dian.call.duration").Single();
        var lag = metrics.Of("impifact.outbox.queue.lag").Single(measurement =>
            measurement.Tag(ImpiFactMetrics.OutboxMessageTypeTag) == OutboxMessageTypes.SendDocumentToDian);
        Assert.Multiple(() =>
        {
            Assert.That(metrics.Sum("impifact.documents.issued", ImpiFactMetrics.DocumentKindTag, "invoice"), Is.EqualTo(1));
            Assert.That(metrics.Sum("impifact.documents.accepted", ImpiFactMetrics.DocumentKindTag, "invoice"), Is.EqualTo(1));
            Assert.That(metrics.Sum("impifact.documents.rejected"), Is.Zero);
            Assert.That(call.Tags, Is.EquivalentTo(new Dictionary<string, string?>
            {
                [ImpiFactMetrics.DianOperationTag] = "SendBillSync",
                [ImpiFactMetrics.DianOutcomeTag] = "answered",
            }));
            Assert.That(call.Value, Is.GreaterThanOrEqualTo(0));
            Assert.That(metrics.Sum(
                "impifact.outbox.messages.processed", ImpiFactMetrics.OutboxMessageTypeTag, OutboxMessageTypes.SendDocumentToDian), Is.EqualTo(1));
            Assert.That(lag.Value, Is.GreaterThanOrEqualTo(0));
            AssertNoIdentifyingTags(metrics, issuer, document.Id);
        });
    }

    [Test]
    public async Task An_invoice_rejected_by_its_rules_counts_as_issued_and_rejected()
    {
        await using var host = await DianSendHost.StartAsync();
        using var metrics = new MetricRecorder(host.Services);
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(false, "99", DianReplies.Rejection));

        await host.RunOutboxAsync();

        Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Rejected));
        Assert.Multiple(() =>
        {
            Assert.That(metrics.Sum("impifact.documents.issued", ImpiFactMetrics.DocumentKindTag, "invoice"), Is.EqualTo(1));
            Assert.That(metrics.Sum("impifact.documents.rejected", ImpiFactMetrics.DocumentKindTag, "invoice"), Is.EqualTo(1));
            Assert.That(metrics.Sum("impifact.documents.accepted"), Is.Zero);
            AssertNoIdentifyingTags(metrics, issuer, document.Id);
        });
    }

    [Test]
    public async Task A_decision_from_a_later_status_poll_counts_once_and_the_poll_call_is_measured()
    {
        await using var host = await DianSendHost.StartAsync();
        using var metrics = new MetricRecorder(host.Services);
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(false, "98"));
        await host.RunOutboxAsync();

        Assert.That(metrics.Sum("impifact.documents.accepted"), Is.Zero, "In process (98) is not a decision.");

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        host.Dian.Reply(DianReplies.GetStatus(true, "00"));
        await host.RunOutboxAsync();

        Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Accepted));
        Assert.Multiple(() =>
        {
            Assert.That(metrics.Sum("impifact.documents.issued"), Is.EqualTo(1), "The poll does not issue the document again.");
            Assert.That(metrics.Sum("impifact.documents.accepted", ImpiFactMetrics.DocumentKindTag, "invoice"), Is.EqualTo(1));
            Assert.That(metrics.Of("impifact.dian.call.duration").Select(measurement => measurement.Tag(ImpiFactMetrics.DianOperationTag)),
                Is.EqualTo(new[] { "SendBillSync", "GetStatus" }));
            Assert.That(metrics.Sum(
                "impifact.outbox.messages.processed", ImpiFactMetrics.OutboxMessageTypeTag, OutboxMessageTypes.PollDianStatus), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task A_dian_that_cannot_be_reached_counts_a_not_sent_call_and_a_retried_message_but_no_issued_document()
    {
        await using var host = await DianSendHost.StartAsync();
        using var metrics = new MetricRecorder(host.Services);
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Otherwise = () => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused");

        await host.RunOutboxAsync();

        Assert.That((await host.DocumentAsync(document.Id)).Status, Is.EqualTo(DocumentStatus.Queued));
        Assert.Multiple(() =>
        {
            Assert.That(metrics.Of("impifact.dian.call.duration").Single().Tag(ImpiFactMetrics.DianOutcomeTag), Is.EqualTo("not_sent"));
            Assert.That(metrics.Of("impifact.outbox.messages.failed").Single().Tags, Is.EquivalentTo(new Dictionary<string, string?>
            {
                [ImpiFactMetrics.OutboxMessageTypeTag] = OutboxMessageTypes.SendDocumentToDian,
                [ImpiFactMetrics.OutboxOutcomeTag] = "retry",
            }));
            Assert.That(metrics.Sum("impifact.documents.issued"), Is.Zero);
            Assert.That(metrics.Sum("impifact.outbox.messages.processed"), Is.Zero);
        });
    }

    [Test]
    public async Task Each_outbox_message_is_one_trace_span_that_contains_its_database_commands()
    {
        await using var host = await DianSendHost.StartAsync();
        var issuer = await host.CreateIssuerAsync();
        await host.CreateDocumentAsync(issuer);
        var message = (await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendDocumentToDian)).Single();
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));
        var stopped = new System.Collections.Concurrent.ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "ImpiFact" || source.Name == TestDatabase.DriverActivitySource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        await host.RunOutboxAsync();

        var span = stopped.Single(activity => activity.GetTagItem("messaging.message.id") as string == message.Id.ToString());
        var commands = stopped.Where(activity => activity.Source.Name == TestDatabase.DriverActivitySource && activity.TraceId == span.TraceId).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(span.DisplayName, Is.EqualTo("outbox SendDocumentToDian"));
            Assert.That(span.Kind, Is.EqualTo(ActivityKind.Consumer));
            Assert.That(span.GetTagItem("messaging.destination.name"), Is.EqualTo(OutboxMessageTypes.SendDocumentToDian));
            // La DIAN falsa reemplaza el manejador HTTP real, que es el que crea los tramos HTTP; los de la base sí están.
            Assert.That(commands, Is.Not.Empty, "The handler's database commands belong to the trace of the message.");
            Assert.That(commands.Select(activity => activity.ParentSpanId), Has.All.Not.EqualTo(default(ActivitySpanId)));
        });
    }

    private static void AssertNoIdentifyingTags(MetricRecorder metrics, TestIssuer issuer, Guid documentId)
    {
        var values = metrics.Measurements.SelectMany(measurement => measurement.Tags.Values).ToList();
        Assert.That(values, Has.None.Contains(issuer.Nit).And.None.Contains(issuer.CompanyId.ToString()).And.None.Contains(documentId.ToString()));
    }
}
