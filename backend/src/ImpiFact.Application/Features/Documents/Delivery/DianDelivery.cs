using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Application.Abstractions.Dian;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Notifications;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Application.Features.Documents.Email;
using ImpiFact.Dian.Soap;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Documents.Delivery;

/// <summary>Mensaje <c>PollDianStatus</c>: volver a consultar en la DIAN el estado de un documento enviado.</summary>
/// <param name="Query">
/// Con qué se consulta: GetStatus con el CUFE del documento (código único de la factura electrónica, o CUDE en las
/// notas) o GetStatusZip con la ZipKey (clave del envío asíncrono del set de pruebas) guardada en <c>track_id</c>.
/// </param>
public sealed record PollDianStatusPayload(Guid DocumentId, DianStatusQuery Query);

/// <summary>Operación con que se consulta el estado de un documento en la DIAN.</summary>
public enum DianStatusQuery
{
    /// <summary>GetStatus con el CUFE o CUDE del documento.</summary>
    GetStatus = 1,

    /// <summary>GetStatusZip con la ZipKey que devolvió SendTestSetAsync.</summary>
    GetStatusZip = 2,
}

/// <summary>Tipos de los avisos del panel que deja el envío a la DIAN (<c>notifications.type</c>).</summary>
public static class DianNotificationTypes
{
    /// <summary>La DIAN rechazó el mensaje sin evaluar el documento (SOAP Fault del que envía).</summary>
    public const string Refused = "DianSubmissionRefused";

    /// <summary>La DIAN no confirmó el documento en las 24 horas siguientes al envío.</summary>
    public const string Unconfirmed = "DocumentUnconfirmed";

    /// <summary>La DIAN revisó el documento y lo rechazó (reglas de validación o número ya usado).</summary>
    public const string Rejected = "DocumentRejected";

    /// <summary>
    /// An invoice type 04 is still pending when the Worker stops retrying alone (48 hours after it was stored): only
    /// <c>send-pending</c> sends it from then on.
    /// </summary>
    public const string ContingencyPending = "ContingencyInvoicePending";
}

/// <summary>Servicio de la DIAN que corresponde al ambiente del documento y el certificado con que se firma el sobre.</summary>
internal sealed record DianEndpoint(string Url, X509Certificate2 Certificate);

/// <summary>
/// La DIAN no recibió el documento (no se alcanzó a enviar, el servicio no estaba disponible o falló de su lado). Es
/// una falla transitoria: el Worker reintenta el mensaje con retroceso y el documento sigue en <c>Queued</c>.
/// </summary>
public sealed class DianNotDeliveredException(string message) : Exception(message);

/// <summary>
/// Piezas comunes del envío a la DIAN (la autoridad tributaria de Colombia) y de la consulta de estado (Arquitectura
/// detallada, secciones 6.4 y 7): registra cada llamada en <c>dian_submissions</c> con la solicitud y la respuesta en
/// <c>document_files</c>, aplica la decisión de la DIAN al documento y programa las consultas siguientes.
/// </summary>
/// <remarks>
/// <see cref="BeginAsync"/> guarda de inmediato la fila del intento (y lo que el contexto tenga pendiente), así una
/// caída a mitad de la llamada deja rastro del intento. Por eso ningún llamador cambia el estado del documento antes
/// de una llamada: el estado y el mensaje que programa la consulta siguiente se guardan juntos, al final.
/// </remarks>
internal sealed class DianDelivery(
    IDianWebService dian,
    IDocumentDelivery documents,
    IDocumentFiles files,
    IDocumentStore store,
    IOutbox outbox,
    ICompanyNotifications notifications,
    IPlanGate plans,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<DianDelivery> logger)
{
    private const int TrackIdMaxLength = 64;
    private const int StatusCodeMaxLength = 10;
    private const int StatusDescriptionMaxLength = 500;

    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>
    /// Agrega la fila del intento (número siguiente del documento) con el ZIP enviado, si lo hay, y la guarda.
    /// </summary>
    /// <param name="zipKey">ZipKey del envío cuando la operación es GetStatusZip; se guarda en <c>track_id</c>.</param>
    public async Task<DianSubmission> BeginAsync(
        Document document, DianOperation operation, string? zipKey, byte[]? sentZip, CancellationToken ct)
    {
        var attempt = await documents.NextSubmissionAttemptAsync(document.CompanyId, document.Id, ct);
        Guid? requestFileId = null;
        if (sentZip is not null)
        {
            requestFileId = (await files.AddAsync(
                new NewDocumentFile(document.CompanyId, document.Id, DocumentFileKind.SentZip, sentZip, "application/zip", attempt), ct)).Id;
        }

        var submission = new DianSubmission
        {
            Id = Guid.CreateVersion7(),
            CompanyId = document.CompanyId,
            DocumentId = document.Id,
            Attempt = attempt,
            Operation = operation,
            TypeEnvironmentId = document.TypeEnvironmentId,
            TrackId = Truncate(zipKey, TrackIdMaxLength),
            ZipKey = Truncate(zipKey, TrackIdMaxLength),
            RequestFileId = requestFileId,
            StartedAt = clock.UtcNow,
        };
        documents.AddSubmission(submission);
        await unitOfWork.SaveChangesAsync(ct);
        return submission;
    }

    /// <summary>
    /// Llama a la DIAN y anota en el intento lo que respondió (sin guardar todavía): código HTTP, resultado, duración y
    /// los archivos de la respuesta (el sobre como <see cref="DocumentFileKind.DianResponse"/> y, si viene, el
    /// ApplicationResponse, el XML de respuesta firmado por la DIAN).
    /// </summary>
    public async Task<DianAnswer> CallAsync(DianSubmission submission, DianSoapRequest request, DianEndpoint endpoint, CancellationToken ct)
    {
        var call = await dian.CallAsync(request, endpoint.Url, endpoint.Certificate, ct);
        await RecordAsync(submission, call, ct);
        var answer = DianAnswer.From(call);
        logger.LogInformation(
            "DIAN {Operation} for document {DocumentId} (attempt {Attempt}) ended as {Outcome} with HTTP {HttpStatus} in {DurationMs} ms: {Answer}.",
            request.OperationName, submission.DocumentId, submission.Attempt, call.Outcome, call.HttpStatus, submission.DurationMs,
            answer.GetType().Name);
        if (call.Failure is not null)
        {
            logger.LogWarning("DIAN {Operation} for document {DocumentId} got no answer: {Failure}", request.OperationName, submission.DocumentId, call.Failure);
        }

        return answer;
    }

    /// <summary>
    /// Regla 90 (Arquitectura detallada, sección 7, paso 10): la DIAN ya procesó un documento con ese número (NIT del
    /// emisor, prefijo y número), así que se consulta el estado del nuestro con GetStatus y su CUFE y se toma esa
    /// respuesta. Si GetStatus responde el código 66 (la DIAN no tiene ese CUFE), el documento que la DIAN procesó con
    /// ese número es otro: el nuestro queda rechazado con <see cref="NumberUsedByAnotherDocument"/>, porque si fuera el
    /// nuestro GetStatus devolvería su decisión. Todo lo demás que no sea una decisión (otra vez la regla 90, sin
    /// respuesta, otro «en proceso» como el 98, un error) queda como «en proceso» o «sin respuesta» y se vuelve a
    /// consultar después; nunca se reenvía.
    /// </summary>
    /// <returns><see cref="DianAnswer.Decided"/>, <see cref="DianAnswer.InProcess"/> o <see cref="DianAnswer.NoAnswer"/>.</returns>
    public async Task<DianAnswer> ReconcileAsync(Document document, DianEndpoint endpoint, CancellationToken ct)
    {
        var cufe = document.Cufe ?? throw new PermanentOutboxFailureException($"Document {document.Id} has no CUFE to query its status.");
        var submission = await BeginAsync(document, DianOperation.GetStatus, zipKey: null, sentZip: null, ct);
        return await CallAsync(submission, new GetStatusRequest(cufe), endpoint, ct) switch
        {
            DianAnswer.InProcess { Result: { } unknown } when DianAnswer.IsUnknownDocument(unknown) => NumberUsedByAnotherDocument(document, unknown),
            (DianAnswer.Decided or DianAnswer.InProcess or DianAnswer.NoAnswer) and var answer => answer,
            DianAnswer.AlreadyProcessed processed => new DianAnswer.InProcess(processed.Result),
            var other => new DianAnswer.NoAnswer($"GetStatus after rule 90 did not decide: {other.GetType().Name}."),
        };
    }

    /// <summary>
    /// La misma regla de <see cref="ReconcileAsync"/> para una consulta de estado posterior: si GetStatus con el CUFE
    /// respondió el código 66 y algún envío del documento (SendBillSync o SendTestSetAsync) recibió antes la regla 90,
    /// el número lo usó otro documento y el nuestro queda rechazado. Así se resuelve también un documento que quedó
    /// sin confirmar antes de esta regla. Cualquier otra respuesta se devuelve igual.
    /// </summary>
    public async Task<DianAnswer> SettleUnknownAfterRule90Async(Document document, DianAnswer answer, CancellationToken ct)
    {
        if (answer is not DianAnswer.InProcess { Result: { } unknown } || !DianAnswer.IsUnknownDocument(unknown))
        {
            return answer;
        }

        var messages = await documents.InvalidSendMessagesAsync(document.CompanyId, document.Id, ct);
        return DianAnswer.IsRule90(messages) ? NumberUsedByAnotherDocument(document, unknown) : answer;
    }

    /// <summary>
    /// Rechazo porque la DIAN procesó otro documento con el mismo número: el resultado de GetStatus (con su código 66,
    /// el último que respondió la DIAN) con la explicación para el usuario en la descripción.
    /// </summary>
    private DianAnswer.Decided NumberUsedByAnotherDocument(Document document, DianResult unknown)
    {
        logger.LogWarning(
            "The DIAN already processed another document numbered {FullNumber} (rule 90) and does not know the CUFE of document {DocumentId}; it is rejected.",
            document.FullNumber, document.Id);
        return new DianAnswer.Decided(
            DocumentStatus.Rejected,
            unknown with
            {
                StatusDescription = $"El número {document.FullNumber} ya fue usado en la DIAN por otro documento. " +
                    "Revise la numeración de la resolución y emita la factura de nuevo.",
                // El «TrackId no existe» de GetStatus no explica nada al usuario: el motivo es la descripción.
                ErrorMessages = [],
            });
    }

    /// <summary>
    /// Aplica la decisión de la DIAN a un documento enviado (<c>Sent</c> o <c>Unconfirmed</c>): estado final, código y
    /// descripción de la DIAN y, si quedó aceptado, la hora de aceptación, el consumo del plan y lo que sigue a la
    /// aceptación (ver <see cref="ScheduleAfterAcceptanceAsync"/>), todo para el mismo guardado.
    /// </summary>
    /// <remarks>
    /// El consumo cuenta una sola vez por documento: solo un documento en <c>Sent</c> o <c>Unconfirmed</c> llega aquí
    /// (<see cref="Document.MoveTo"/> no deja aceptar dos veces), y si otro proceso lo aceptó al mismo tiempo, el
    /// guardado falla por la versión de fila del documento y la cuenta, que va en ese guardado, tampoco queda. Los
    /// documentos del set de pruebas de habilitación (los que tienen la ZipKey en <c>track_id</c>) no cuentan, como en
    /// el PHP, que nunca los dejaba en estado aceptado.
    /// </remarks>
    /// <param name="endpoint">Servicio y certificado de la llamada; con ese certificado se firma el AttachedDocument.</param>
    public async Task DecideAsync(Document document, DianAnswer.Decided decided, DianEndpoint endpoint, CancellationToken ct)
    {
        document.MoveTo(decided.Status);
        Stamp(document, decided.Result);
        if (decided.Status is DocumentStatus.Accepted or DocumentStatus.AcceptedWithWarnings)
        {
            document.AcceptedAt = clock.UtcNow;
            if (document.TrackId is null)
            {
                // Each family counts in its own plan quota: invoices and notes (types 1 to 5) in qty_docs_invoice, the support
                // document and its adjustment note (11 and 13) in qty_docs_ds.
                await plans.CountAcceptedAsync(document.CompanyId, DocumentFamilies.Of(document.DocumentTypeId), ct);
            }

            await ScheduleAfterAcceptanceAsync(document, decided.Result, endpoint.Certificate, ct);
        }
        else if (decided.Status == DocumentStatus.Rejected)
        {
            NotifyRejected(document, decided.Result);
        }
    }

    /// <summary>Reglas de la DIAN que se copian en el aviso de rechazo; el resto queda en el detalle del documento.</summary>
    private const int RejectionReasonsInNotice = 3;

    /// <summary>Largo máximo del cuerpo de un aviso (<c>notifications.body</c>).</summary>
    private const int NoticeBodyMaxLength = 1000;

    /// <summary>
    /// Avisa a la empresa que la DIAN rechazó un documento, con su número y el motivo: las primeras reglas que la DIAN
    /// incumplió (son textos para el usuario, por ejemplo «Regla: FAU14, Rechazo: …») o, si no trae reglas, la
    /// descripción del resultado, que en el caso del número ya usado ya dice qué hacer.
    /// </summary>
    private void NotifyRejected(Document document, DianResult result)
    {
        var rules = result.ErrorMessages.Where(message => !string.IsNullOrWhiteSpace(message)).Take(RejectionReasonsInNotice).ToList();
        var reason = rules.Count > 0
            ? string.Join(" ", rules) + " Corrija el documento y emita uno nuevo."
            : result.StatusDescription ?? "La DIAN no indicó el motivo; revise el detalle del documento.";
        var body = $"La DIAN rechazó el documento {document.FullNumber}. Motivo: {reason}";
        notifications.Add(
            document.CompanyId,
            DianNotificationTypes.Rejected,
            "La DIAN rechazó un documento",
            body.Length <= NoticeBodyMaxLength ? body : body[..(NoticeBodyMaxLength - 1)] + "…");
    }

    /// <summary>Anota el código y la descripción del último resultado de la DIAN, sin cambiar el estado.</summary>
    public static void Stamp(Document document, DianResult? result)
    {
        if (result is null)
        {
            return;
        }

        document.DianStatusCode = Truncate(result.StatusCode, StatusCodeMaxLength) ?? document.DianStatusCode;
        document.DianStatusDescription = Truncate(result.StatusDescription, StatusDescriptionMaxLength) ?? document.DianStatusDescription;
    }

    /// <summary>
    /// La DIAN rechazó el mensaje sin evaluar el documento (SOAP Fault del que envía): el documento pasa de
    /// <c>Queued</c> a <c>Failed</c> y la empresa recibe un aviso.
    /// </summary>
    public void Refuse(Document document, string reason)
    {
        document.MoveTo(DocumentStatus.Failed);
        document.DianStatusDescription = Truncate(reason, StatusDescriptionMaxLength);
        notifications.Add(
            document.CompanyId,
            DianNotificationTypes.Refused,
            "La DIAN no recibió un documento",
            $"La DIAN rechazó el envío del documento {document.FullNumber} sin revisarlo, por ejemplo por el certificado o el software " +
            "configurados, y el documento quedó como fallido. Revise la configuración y emita un documento nuevo.");
    }

    /// <summary>
    /// The DIAN refused the message of an invoice type 04 without evaluating it (a SOAP Fault of the sender, for example the
    /// certificate or the software): unlike <see cref="Refuse"/>, the document stays <c>ContingencyPending</c>, because it
    /// was already issued and must still reach the DIAN, and the company gets a notice to fix the setup and send it again.
    /// </summary>
    public void RefuseContingency(Document document, string reason)
    {
        document.DianStatusDescription = Truncate(reason, StatusDescriptionMaxLength);
        notifications.Add(
            document.CompanyId,
            DianNotificationTypes.Refused,
            "La DIAN no recibió una factura de contingencia",
            $"La DIAN rechazó el envío de la factura de contingencia {document.FullNumber} sin revisarla, por ejemplo por el certificado " +
            "o el software configurados. La factura sigue pendiente: revise la configuración y envíela de nuevo con " +
            "POST /api/v1/documents/invoices/dian-contingency/send-pending.");
    }

    /// <summary>
    /// The Worker stops retrying alone an invoice type 04 that the DIAN has not received (see
    /// <c>ContingencyDelivery.AutomaticWindow</c>): it stays <c>ContingencyPending</c> and the company gets a notice.
    /// </summary>
    public void NotifyContingencyStillPending(Document document)
    {
        notifications.Add(
            document.CompanyId,
            DianNotificationTypes.ContingencyPending,
            "Una factura de contingencia sigue sin transmitirse",
            $"La factura de contingencia {document.FullNumber} sigue sin llegar a la DIAN y ImpiFact dejó de reintentar su envío " +
            "automático. La DIAN da 48 horas para transmitirla después de restablecer su servicio: envíela con " +
            "POST /api/v1/documents/invoices/dian-contingency/send-pending cuando la DIAN esté disponible.");
        logger.LogWarning("Contingency invoice {DocumentId} is still pending after the automatic window; only send-pending sends it now.", document.Id);
    }

    /// <summary>Programa la próxima consulta del estado en la DIAN.</summary>
    public Task SchedulePollAsync(Document document, DianStatusQuery query, DateTimeOffset notBefore, CancellationToken ct) =>
        outbox.EnqueueAsync(
            OutboxMessageTypes.PollDianStatus, new PollDianStatusPayload(document.Id, query), document.CompanyId, notBefore, ct);

    /// <summary>
    /// Se cumplió el plazo de 24 horas sin decisión de la DIAN: el documento pasa a <c>Failed</c> (por
    /// <c>Unconfirmed</c> si estaba en <c>Sent</c>) y la empresa recibe un aviso.
    /// </summary>
    public void GiveUp(Document document)
    {
        if (document.Status == DocumentStatus.Sent)
        {
            document.MoveTo(DocumentStatus.Unconfirmed);
        }

        document.MoveTo(DocumentStatus.Failed);
        notifications.Add(
            document.CompanyId,
            DianNotificationTypes.Unconfirmed,
            "Un documento quedó sin confirmar en la DIAN",
            $"La DIAN no confirmó el estado del documento {document.FullNumber} en las 24 horas siguientes a su envío y el documento " +
            "quedó como fallido. Consulte su estado en la DIAN antes de volver a emitirlo.");
        logger.LogWarning("Document {DocumentId} got no DIAN decision within {Deadline}; it is marked Failed.", document.Id, DianPollSchedule.Deadline);
    }

    /// <summary>
    /// Lo que sigue a la aceptación (Arquitectura detallada, sección 7, paso 9; supuesto C6), en el mismo guardado que
    /// la aceptación: genera y guarda el AttachedDocument (el contenedor XML que se entrega al comprador) firmado con el
    /// certificado de la llamada y encola <c>RenderPdf</c>; el handler del PDF encola después <c>SendEmail</c>. El
    /// consumo del plan lo agrega <see cref="DecideAsync"/>; <c>DocumentAccepted</c> llega en una tarea posterior.
    /// </summary>
    /// <remarks>
    /// La aceptación nunca falla por el AttachedDocument: si la respuesta no trae el ApplicationResponse (por ejemplo,
    /// una respuesta de GetStatus sin XmlBase64Bytes) o el contenedor no se puede armar, queda un aviso en los logs, el
    /// documento queda aceptado sin contenedor y <c>RenderPdf</c> se encola igual. El correo, que necesita el
    /// contenedor, se reintenta y termina fallido (<c>email_status</c>) si sigue sin él; el contenedor se puede
    /// regenerar después (<c>regenerate-attached-document</c>). Una falla al guardar el archivo sí se propaga, como la
    /// de los demás archivos de la respuesta: el mensaje se reintenta.
    /// </remarks>
    private async Task ScheduleAfterAcceptanceAsync(Document document, DianResult result, X509Certificate2 certificate, CancellationToken ct)
    {
        if (await BuildAttachedDocumentAsync(document, result, certificate, ct) is { } attachedDocument)
        {
            await files.AddAsync(
                new NewDocumentFile(document.CompanyId, document.Id, DocumentFileKind.AttachedDocument, Utf8.GetBytes(attachedDocument),
                    "application/xml"), ct);
        }

        await outbox.EnqueueAsync(OutboxMessageTypes.RenderPdf, new RenderPdfPayload(document.Id), document.CompanyId, notBefore: null, ct);
    }

    /// <summary>El AttachedDocument firmado; nulo (con un aviso en los logs) si no se puede armar.</summary>
    private async Task<string?> BuildAttachedDocumentAsync(Document document, DianResult result, X509Certificate2 certificate, CancellationToken ct)
    {
        if (result.XmlBase64Bytes is not { Length: > 0 })
        {
            logger.LogWarning(
                "The DIAN accepted document {DocumentId} without returning its ApplicationResponse; the attached document is not generated.",
                document.Id);
            return null;
        }

        if (await store.ReadFileAsync(document.CompanyId, document.Id, DocumentFileKind.SignedXml, ct) is not { } signedXml)
        {
            logger.LogWarning("Accepted document {DocumentId} has no signed XML; the attached document is not generated.", document.Id);
            return null;
        }

        try
        {
            return AttachedDocumentFactory.BuildSigned(document, Utf8.GetString(signedXml), result, certificate, clock.UtcNow);
        }
        catch (Exception exception) when (exception is ArgumentException or XmlException or CryptographicException)
        {
            logger.LogWarning(
                "The attached document of accepted document {DocumentId} could not be built ({Error}); it is not generated.",
                document.Id, exception.GetType().Name + ": " + exception.Message);
            return null;
        }
    }

    private async Task RecordAsync(DianSubmission submission, DianCall call, CancellationToken ct)
    {
        submission.FinishedAt = clock.UtcNow;
        submission.DurationMs = (int)Math.Min(int.MaxValue, call.Duration.TotalMilliseconds);
        submission.HttpStatus = call.HttpStatus;
        if (call.Body is not null)
        {
            submission.ResponseFileId = (await files.AddAsync(
                new NewDocumentFile(submission.CompanyId, submission.DocumentId, DocumentFileKind.DianResponse, Utf8.GetBytes(call.Body),
                    "application/soap+xml", submission.Attempt), ct)).Id;
        }

        switch (call.Response)
        {
            case DianResultResponse { Results: [var result, ..] }:
                submission.IsValid = result.IsValid;
                submission.DianStatusCode = Truncate(result.StatusCode, StatusCodeMaxLength);
                submission.DianStatusDescription = Truncate(result.StatusDescription, StatusDescriptionMaxLength);
                submission.ErrorMessages = result.ErrorMessages.Count > 0 ? [.. result.ErrorMessages] : null;
                if (!string.IsNullOrWhiteSpace(result.ZipKey))
                {
                    submission.ZipKey = Truncate(result.ZipKey, TrackIdMaxLength);
                    submission.TrackId = submission.ZipKey;
                }

                if (result.XmlBase64Bytes is { Length: > 0 } applicationResponse)
                {
                    await files.AddAsync(
                        new NewDocumentFile(submission.CompanyId, submission.DocumentId, DocumentFileKind.ApplicationResponse, applicationResponse,
                            "application/xml", submission.Attempt), ct);
                }

                break;
            case DianSoapFaultResponse fault:
                submission.DianStatusDescription = Truncate(fault.Reason, StatusDescriptionMaxLength);
                break;
        }
    }

    private static string? Truncate(string? text, int maxLength) =>
        string.IsNullOrEmpty(text) ? null : text.Length <= maxLength ? text : text[..maxLength];
}
