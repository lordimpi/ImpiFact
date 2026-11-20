using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Observability;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Dian.Soap;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Documents.Delivery;

/// <summary>
/// Handler del mensaje <c>PollDianStatus</c> en el Worker (Arquitectura detallada, secciones 7 y 8): vuelve a consultar
/// en la DIAN (la autoridad tributaria de Colombia) un documento enviado que todavía no tiene decisión, con GetStatus y
/// el CUFE (código único de la factura electrónica) o con GetStatusZip y la ZipKey (clave del envío del set de
/// pruebas).
/// </summary>
/// <remarks>
/// Aplica las mismas reglas que el envío (ver <see cref="DianAnswer"/>; con la regla 90 consulta GetStatus, y un
/// código 66 de GetStatus después de un envío con la regla 90 es un rechazo, ver
/// <see cref="DianDelivery.SettleUnknownAfterRule90Async"/>). Mientras
/// no haya decisión se vuelve a programar según <see cref="DianPollSchedule"/>: cada minuto los primeros 10 minutos
/// desde el envío y luego cada 15 minutos; a las 24 horas el documento pasa a <c>Failed</c> con un aviso. Una falla de
/// red o del servicio no se reintenta por el retroceso del Worker: cuenta como una consulta sin decisión, así el plazo
/// de 24 horas manda. Sin un certificado activo (por ejemplo, porque se venció o se reemplazó después del envío) la
/// consulta tampoco se hace y cuenta como una consulta sin decisión. Si el Worker se rinde con el mensaje por
/// cualquier otra falla (<see cref="OnFailedAsync"/>), el documento se vuelve a programar o, pasado el plazo, pasa a
/// <c>Failed</c>: nunca queda en <c>Sent</c> ni en <c>Unconfirmed</c> sin una consulta pendiente. Un documento que ya
/// no está en <c>Sent</c> ni en <c>Unconfirmed</c> no se consulta.
/// </remarks>
internal sealed class PollDianStatusHandler(
    DianDelivery delivery,
    IDocumentDelivery documents,
    IIssuerContextReads issuers,
    ICertificateStore certificates,
    IDianServiceUrls urls,
    IUnitOfWork unitOfWork,
    IClock clock,
    ImpiFactMetrics metrics,
    ILogger<PollDianStatusHandler> logger) : IOutboxMessageHandler
{
    public string MessageType => OutboxMessageTypes.PollDianStatus;

    public async Task HandleAsync(OutboxMessageContext message, CancellationToken ct)
    {
        var payload = message.GetPayload<PollDianStatusPayload>();
        var companyId = message.CompanyId
            ?? throw new PermanentOutboxFailureException($"Outbox message {message.MessageId} has no company.");
        var document = await documents.FindForUpdateAsync(companyId, payload.DocumentId, ct)
            ?? throw new PermanentOutboxFailureException($"Document {payload.DocumentId} does not exist.");
        if (document.Status is not (DocumentStatus.Sent or DocumentStatus.Unconfirmed))
        {
            logger.LogInformation("Document {DocumentId} is already {Status}; its DIAN status is not queried again.", document.Id, document.Status);
            return;
        }

        var (operation, key) = KeyFor(document, payload.Query);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new PermanentOutboxFailureException($"Document {document.Id} has no key for {operation}.");
        }

        var issuer = await issuers.GetAsync(companyId, document.DocumentTypeId, DocumentKind.Invoice, ct)
            ?? throw new PermanentOutboxFailureException($"Company {companyId} does not exist.");
        if (issuer.Certificate?.SecretRef is not { } secretRef)
        {
            logger.LogWarning(
                "Company {CompanyId} has no active certificate; the DIAN status of document {DocumentId} is not queried this time.",
                companyId, document.Id);
            await ContinueAsync(document, payload.Query, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        using var certificate = await certificates.LoadAsync(companyId, secretRef, ct);
        var endpoint = new DianEndpoint(urls.ForEnvironment(document.TypeEnvironmentId), certificate);
        var submission = await delivery.BeginAsync(
            document, operation, operation == DianOperation.GetStatusZip ? key : null, sentZip: null, ct);
        DianSoapRequest request = operation == DianOperation.GetStatusZip ? new GetStatusZipRequest(key) : new GetStatusRequest(key);
        var answer = await delivery.CallAsync(submission, request, endpoint, ct);
        if (answer is DianAnswer.AlreadyProcessed)
        {
            answer = await delivery.ReconcileAsync(document, endpoint, ct);
        }
        else if (operation == DianOperation.GetStatus)
        {
            // Código 66 de un documento cuyo envío recibió la regla 90: el número lo usó otro documento.
            answer = await delivery.SettleUnknownAfterRule90Async(document, answer, ct);
        }

        if (answer is DianAnswer.Decided decided)
        {
            await delivery.DecideAsync(document, decided, endpoint, ct);
        }
        else
        {
            DianDelivery.Stamp(document, (answer as DianAnswer.InProcess)?.Result);
            await ContinueAsync(document, payload.Query, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        if (answer is DianAnswer.Decided)
        {
            metrics.DocumentDecided(document);
        }
    }

    /// <summary>
    /// El Worker se rindió con la consulta (por ejemplo, cinco fallas seguidas de la base, del almacenamiento o al
    /// cargar el certificado): si el documento sigue sin decisión, se programa otra consulta según
    /// <see cref="DianPollSchedule"/> o, pasado el plazo de 24 horas (o sin clave con qué consultar), pasa a
    /// <c>Failed</c> con el aviso <see cref="DianNotificationTypes.Unconfirmed"/>.
    /// </summary>
    /// <remarks>
    /// El Worker lo llama antes de guardar el fallo del mensaje y puede llamarlo otra vez si el proceso se cae en medio;
    /// en ese caso puede quedar una consulta de más, que es inofensiva: la que llegue después de la decisión no hace nada.
    /// </remarks>
    public async Task OnFailedAsync(OutboxMessageContext message, CancellationToken ct)
    {
        var payload = message.GetPayload<PollDianStatusPayload>();
        if (message.CompanyId is not { } companyId
            || await documents.FindForUpdateAsync(companyId, payload.DocumentId, ct) is not
                { Status: DocumentStatus.Sent or DocumentStatus.Unconfirmed } document)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(KeyFor(document, payload.Query).Key))
        {
            delivery.GiveUp(document);
        }
        else
        {
            await ContinueAsync(document, payload.Query, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogWarning(
            "The DIAN status poll of document {DocumentId} failed after every attempt; the document is now {Status}.", document.Id, document.Status);
    }

    private static (DianOperation Operation, string? Key) KeyFor(Document document, DianStatusQuery query) =>
        query == DianStatusQuery.GetStatusZip
            ? (DianOperation.GetStatusZip, document.TrackId)
            : (DianOperation.GetStatus, document.Cufe);

    /// <summary>Sin decisión: programa la próxima consulta o, pasado el plazo, da el documento por fallido.</summary>
    private async Task ContinueAsync(Document document, DianStatusQuery query, CancellationToken ct)
    {
        var now = clock.UtcNow;
        // Sin hora de envío (no debería pasar) el plazo cuenta desde ahora, para que nunca se consulte sin fin.
        document.SentAt ??= now;
        if (DianPollSchedule.Next(document.SentAt.Value, now) is { } next)
        {
            await delivery.SchedulePollAsync(document, query, next, ct);
        }
        else
        {
            delivery.GiveUp(document);
        }
    }
}
