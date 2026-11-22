using System.Diagnostics.Metrics;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Abstractions.Observability;

/// <summary>
/// Métricas de negocio de ImpiFact (Arquitectura detallada, sección 12) en un solo <see cref="Meter"/>,
/// <see cref="MeterName"/>, con <c>System.Diagnostics.Metrics</c>: las exporta OpenTelemetry por OTLP cuando está
/// configurado y se pueden ver en vivo con <c>dotnet-counters monitor --counters ImpiFact</c>.
/// </summary>
/// <remarks>
/// Los nombres de los instrumentos y de sus etiquetas son un contrato con los tableros y las alertas: no se cambian.
/// Ninguna etiqueta lleva datos de una persona o de una empresa (NIT, correos, nombres, ids): solo valores de un
/// conjunto fijo y pequeño, para no exponer datos ni multiplicar las series.
/// </remarks>
public sealed class ImpiFactMetrics
{
    public const string MeterName = "ImpiFact";

    /// <summary>Etiqueta con el tipo de documento: <c>invoice</c>, <c>credit_note</c>, <c>debit_note</c> u <c>other</c>.</summary>
    public const string DocumentKindTag = "document.kind";

    /// <summary>Etiqueta con la operación de la DIAN: <c>SendBillSync</c>, <c>GetStatus</c>, <c>GetNumberingRange</c>…</summary>
    public const string DianOperationTag = "dian.operation";

    /// <summary>Etiqueta con el resultado de la llamada a la DIAN: <c>answered</c>, <c>not_sent</c> o <c>no_answer</c>.</summary>
    public const string DianOutcomeTag = "dian.outcome";

    /// <summary>Etiqueta con el tipo de mensaje de la bandeja de salida (<c>SendDocumentToDian</c>, <c>SendEmail</c>…).</summary>
    public const string OutboxMessageTypeTag = "outbox.message_type";

    /// <summary>Etiqueta de un mensaje fallido: <c>retry</c> (se reintenta) o <c>failed</c> (no se reintenta más).</summary>
    public const string OutboxOutcomeTag = "outbox.outcome";

    private readonly Counter<long> documentsIssued;
    private readonly Counter<long> documentsAccepted;
    private readonly Counter<long> documentsRejected;
    private readonly Histogram<double> dianCallDuration;
    private readonly Counter<long> outboxProcessed;
    private readonly Counter<long> outboxFailed;
    private readonly Histogram<double> outboxLag;
    private readonly Counter<long> emailsSent;
    private readonly Counter<long> emailsFailed;

    public ImpiFactMetrics(IMeterFactory meters)
    {
        var meter = meters.Create(MeterName);
        documentsIssued = meter.CreateCounter<long>(
            "impifact.documents.issued", "{document}", "Documents the DIAN received for the first time (they left Queued).");
        documentsAccepted = meter.CreateCounter<long>(
            "impifact.documents.accepted", "{document}", "Documents the DIAN accepted, with or without warnings.");
        documentsRejected = meter.CreateCounter<long>(
            "impifact.documents.rejected", "{document}", "Documents the DIAN reviewed and rejected.");
        dianCallDuration = meter.CreateHistogram<double>(
            "impifact.dian.call.duration", "s", "Duration of a DIAN web service call, with its retries.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.25, 0.5, 1, 2, 5, 10, 20, 30, 60, 120] });
        outboxProcessed = meter.CreateCounter<long>(
            "impifact.outbox.messages.processed", "{message}", "Outbox messages whose handler finished.");
        outboxFailed = meter.CreateCounter<long>(
            "impifact.outbox.messages.failed", "{message}", "Outbox message attempts that failed.");
        outboxLag = meter.CreateHistogram<double>(
            "impifact.outbox.queue.lag", "s", "Time an outbox message waited after it was due until a worker took it.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.5, 1, 2, 5, 10, 30, 60, 120, 300, 600, 1800] });
        emailsSent = meter.CreateCounter<long>("impifact.emails.sent", "{email}", "Emails the mail server accepted.");
        emailsFailed = meter.CreateCounter<long>("impifact.emails.failed", "{email}", "Emails that could not be sent.");
    }

    /// <summary>Un documento salió de <c>Queued</c>: la DIAN lo recibió por primera vez.</summary>
    public void DocumentIssued(Document document) => documentsIssued.Add(1, KindOf(document));

    /// <summary>
    /// Cuenta la decisión de la DIAN si el documento quedó aceptado o rechazado; cualquier otro estado no cuenta. Se
    /// llama después de guardar la decisión.
    /// </summary>
    public void DocumentDecided(Document document)
    {
        switch (document.Status)
        {
            case DocumentStatus.Accepted or DocumentStatus.AcceptedWithWarnings:
                documentsAccepted.Add(1, KindOf(document));
                break;
            case DocumentStatus.Rejected:
                documentsRejected.Add(1, KindOf(document));
                break;
        }
    }

    /// <param name="outcome"><c>answered</c>, <c>not_sent</c> o <c>no_answer</c>.</param>
    public void DianCall(string operation, string outcome, TimeSpan duration) =>
        dianCallDuration.Record(duration.TotalSeconds, new(DianOperationTag, operation), new(DianOutcomeTag, outcome));

    public void OutboxMessageTaken(string messageType, TimeSpan lag) =>
        outboxLag.Record(Math.Max(0, lag.TotalSeconds), new KeyValuePair<string, object?>(OutboxMessageTypeTag, messageType));

    public void OutboxMessageProcessed(string messageType) =>
        outboxProcessed.Add(1, new KeyValuePair<string, object?>(OutboxMessageTypeTag, messageType));

    /// <param name="final">Si el mensaje ya no se reintenta.</param>
    public void OutboxMessageFailed(string messageType, bool final) =>
        outboxFailed.Add(1, new(OutboxMessageTypeTag, messageType), new(OutboxOutcomeTag, final ? "failed" : "retry"));

    public void EmailSent() => emailsSent.Add(1);

    public void EmailFailed() => emailsFailed.Add(1);

    /// <summary>Tipo de documento de la DIAN (catálogo <c>type_documents</c>) como etiqueta.</summary>
    public static string KindName(int documentTypeId) => documentTypeId switch
    {
        // Factura de venta nacional, de exportación y de contingencia (tipos 03 y 04, este con el id 12).
        1 or 2 or 3 or 12 => "invoice",
        4 => "credit_note",
        5 => "debit_note",
        _ => "other",
    };

    private static KeyValuePair<string, object?> KindOf(Document document) => new(DocumentKindTag, KindName(document.DocumentTypeId));
}
