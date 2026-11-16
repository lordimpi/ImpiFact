using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Messaging;
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
/// Aplica las mismas reglas que el envío (ver <see cref="DianAnswer"/>; con la regla 90 consulta GetStatus). Mientras
/// no haya decisión se vuelve a programar según <see cref="DianPollSchedule"/>: cada minuto los primeros 10 minutos
/// desde el envío y luego cada 15 minutos; a las 24 horas el documento pasa a <c>Failed</c> con un aviso. Una falla de
/// red o del servicio no se reintenta por el retroceso del Worker: cuenta como una consulta sin decisión, así el plazo
/// de 24 horas manda. Un documento que ya no está en <c>Sent</c> ni en <c>Unconfirmed</c> no se consulta.
/// </remarks>
internal sealed class PollDianStatusHandler(
    DianDelivery delivery,
    IDocumentDelivery documents,
    IIssuerContextReads issuers,
    ICertificateStore certificates,
    IDianServiceUrls urls,
    IUnitOfWork unitOfWork,
    IClock clock,
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

        var (operation, key) = payload.Query == DianStatusQuery.GetStatusZip
            ? (DianOperation.GetStatusZip, document.TrackId)
            : (DianOperation.GetStatus, document.Cufe);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new PermanentOutboxFailureException($"Document {document.Id} has no key for {operation}.");
        }

        var issuer = await issuers.GetAsync(companyId, document.DocumentTypeId, DocumentKind.Invoice, ct)
            ?? throw new PermanentOutboxFailureException($"Company {companyId} does not exist.");
        var secretRef = issuer.Certificate?.SecretRef
            ?? throw new PermanentOutboxFailureException($"Company {companyId} has no active certificate.");

        var submission = await delivery.BeginAsync(
            document, operation, operation == DianOperation.GetStatusZip ? key : null, sentZip: null, ct);
        DianSoapRequest request = operation == DianOperation.GetStatusZip ? new GetStatusZipRequest(key) : new GetStatusRequest(key);
        using var certificate = await certificates.LoadAsync(companyId, secretRef, ct);
        var endpoint = new DianEndpoint(urls.ForEnvironment(document.TypeEnvironmentId), certificate);
        var answer = await delivery.CallAsync(submission, request, endpoint, ct);
        if (answer is DianAnswer.AlreadyProcessed)
        {
            answer = await delivery.ReconcileAsync(document, endpoint, ct);
        }

        if (answer is DianAnswer.Decided decided)
        {
            delivery.Decide(document, decided);
        }
        else
        {
            DianDelivery.Stamp(document, (answer as DianAnswer.InProcess)?.Result);
            var sentAt = document.SentAt ?? submission.StartedAt;
            if (DianPollSchedule.Next(sentAt, clock.UtcNow) is { } next)
            {
                await delivery.SchedulePollAsync(document, payload.Query, next, ct);
            }
            else
            {
                delivery.GiveUp(document);
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}
