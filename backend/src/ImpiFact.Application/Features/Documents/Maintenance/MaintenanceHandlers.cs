using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Delivery;
using ImpiFact.Application.Features.Documents.Email;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Application.Features.Documents.Queries;
using ImpiFact.Application.Features.Documents.Queries.DianStatus;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Maintenance;

/// <summary>Mensaje <c>RegeneratePdf</c>: generar otra revisión del PDF de un documento aceptado.</summary>
public sealed record RegeneratePdfPayload(Guid DocumentId);

/// <summary>Mensaje <c>RegenerateAttachedDocument</c>: armar y firmar otra revisión del AttachedDocument de un documento aceptado.</summary>
public sealed record RegenerateAttachedDocumentPayload(Guid DocumentId);

/// <summary>Encola la regeneración del PDF; responde de inmediato.</summary>
internal sealed class RegeneratePdfHandler(IDocumentLookups lookups, IOutbox outbox, ITenantContext tenant)
    : IHandler<RegeneratePdfCommand, MaintenanceQueuedResponse>
{
    public async Task<Result<MaintenanceQueuedResponse>> HandleAsync(RegeneratePdfCommand request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
        {
            return DocumentErrors.NotFound;
        }

        var found = await DocumentSelection.FindAcceptedAsync(lookups, companyId, request, ct);
        if (!found.IsSuccess)
        {
            return found.Error;
        }

        var document = found.Value;
        await outbox.EnqueueAsync(OutboxMessageTypes.RegeneratePdf, new RegeneratePdfPayload(document.Id), companyId, notBefore: null, ct);
        return Queued($"La regeneración del PDF del documento {document.FullNumber} quedó en cola.", document);
    }

    internal static MaintenanceQueuedResponse Queued(string message, DocumentRef document) =>
        new() { Message = message, Document = UtilityDocumentResponse.From(document) };
}

/// <summary>
/// Encola la regeneración del AttachedDocument. Antes revisa lo que el Worker va a necesitar y que no se arregla
/// reintentando: el ApplicationResponse guardado y un certificado activo con qué firmar.
/// </summary>
internal sealed class RegenerateAttachedDocumentHandler(
    IDocumentLookups lookups, IDocumentFileReads files, IIssuerContextReads issuers, IOutbox outbox, ITenantContext tenant)
    : IHandler<RegenerateAttachedDocumentCommand, MaintenanceQueuedResponse>
{
    public async Task<Result<MaintenanceQueuedResponse>> HandleAsync(RegenerateAttachedDocumentCommand request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
        {
            return DocumentErrors.NotFound;
        }

        var found = await DocumentSelection.FindAcceptedAsync(lookups, companyId, request, ct);
        if (!found.IsSuccess)
        {
            return found.Error;
        }

        var document = found.Value;
        if (await files.ReadLatestAsync(companyId, document.Id, DocumentFileKind.ApplicationResponse, ct) is not { Length: > 0 })
        {
            return MaintenanceErrors.ApplicationResponseMissing(document.FullNumber);
        }

        if ((await issuers.GetAsync(companyId, document.DocumentTypeId, DocumentFamilies.Of(document.DocumentTypeId), ct))?.Certificate is null)
        {
            return IssueErrors.CertificateMissing;
        }

        await outbox.EnqueueAsync(
            OutboxMessageTypes.RegenerateAttachedDocument, new RegenerateAttachedDocumentPayload(document.Id), companyId, notBefore: null, ct);
        return RegeneratePdfHandler.Queued($"La regeneración del AttachedDocument del documento {document.FullNumber} quedó en cola.", document);
    }
}

/// <summary>
/// Encola otra vez <c>SendEmail</c> con su mismo mensaje (<see cref="SendEmailPayload"/>) y deja <c>email_status</c> sin
/// enviar, para que el handler del correo no lo dé por enviado. Antes revisa con las reglas del correo
/// (<see cref="DocumentEmailRecipients"/>) que el documento tenga a quién enviarlo, así la ruta no responde «en cola»
/// para un correo que el Worker no va a enviar.
/// </summary>
internal sealed class SendDocumentEmailHandler(
    IDocumentLookups lookups,
    IDocumentDelivery documents,
    IDocumentFileReads files,
    IIssuerContextReads issuers,
    IOutbox outbox,
    ITenantContext tenant) : IHandler<SendDocumentEmailCommand, MaintenanceQueuedResponse>
{
    public async Task<Result<MaintenanceQueuedResponse>> HandleAsync(SendDocumentEmailCommand request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
        {
            return DocumentErrors.NotFound;
        }

        var found = await DocumentSelection.FindAcceptedAsync(lookups, companyId, request, ct);
        if (!found.IsSuccess || await documents.FindForUpdateAsync(companyId, found.Value.Id, ct) is not { } document)
        {
            return found.IsSuccess ? DocumentErrors.NotFound : found.Error;
        }

        var emailRequest = DocumentEmailRequest.Parse(await files.ReadLatestAsync(companyId, document.Id, DocumentFileKind.RequestJson, ct));
        var companyEmail = (await issuers.GetAsync(companyId, document.DocumentTypeId, DocumentFamilies.Of(document.DocumentTypeId), ct))?.Company.Email;
        var recipients = DocumentEmailRecipients.Resolve(
            emailRequest, document.CustomerSnapshot.IdentificationNumber, document.CustomerSnapshot.Email, companyEmail);
        if (recipients.Skip is { } reason)
        {
            return reason switch
            {
                EmailSkipReason.NotRequested => MaintenanceErrors.EmailNotRequested,
                EmailSkipReason.FinalConsumer => MaintenanceErrors.EmailFinalConsumer,
                _ => MaintenanceErrors.EmailNoRecipient,
            };
        }

        document.EmailStatus = EmailStatus.NotSent;
        await outbox.EnqueueAsync(OutboxMessageTypes.SendEmail, new SendEmailPayload(document.Id), companyId, notBefore: null, ct);
        return RegeneratePdfHandler.Queued($"El envío del correo del documento {document.FullNumber} quedó en cola.", found.Value);
    }
}
