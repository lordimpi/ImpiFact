using System.Text.Json.Serialization;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Queries;
using ImpiFact.Application.Features.Documents.Queries.DianStatus;

namespace ImpiFact.Application.Features.Documents.Maintenance;

/// <summary>
/// <c>POST /api/v1/documents/{id}/pdf/regenerate</c> (el <c>regeneratepdf</c> del PHP, carpeta 10 del Postman): vuelve a
/// generar el PDF de un documento aceptado. Se encola <c>RegeneratePdf</c> y el Worker guarda una revisión nueva del PDF
/// con los datos guardados del documento.
/// </summary>
public sealed record RegeneratePdfCommand(Guid DocumentId) : ICommand;

/// <summary>
/// <c>POST /api/v1/documents/{id}/attached-document/regenerate</c> (carpeta 05 del Postman; no está en el PHP
/// Community): vuelve a armar y firmar el AttachedDocument (contenedor electrónico) de un documento aceptado con el
/// ApplicationResponse (respuesta firmada de la DIAN) guardado. Se encola <c>RegenerateAttachedDocument</c>; el Worker
/// guarda una revisión nueva.
/// </summary>
public sealed record RegenerateAttachedDocumentCommand(Guid DocumentId) : ICommand;

/// <summary>
/// <c>POST /api/v1/documents/{id}/email</c> (el <c>send-email</c> del PHP, carpeta 05 del Postman): vuelve a enviar el
/// correo de un documento aceptado. Se encola <c>SendEmail</c> con los destinatarios de la solicitud con que se emitió,
/// aunque el correo ya se hubiera enviado.
/// </summary>
public sealed record SendDocumentEmailCommand(Guid DocumentId) : ICommand;

/// <summary>Respuesta inmediata de una tarea encolada: el Worker la hace después (decisión C5 de la fase 4).</summary>
public sealed record MaintenanceQueuedResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";

    [JsonPropertyName("document")]
    public UtilityDocumentResponse Document { get; init; } = null!;
}

/// <summary>Errores esperados de las rutas de mantenimiento.</summary>
public static class MaintenanceErrors
{
    /// <summary>La respuesta de la DIAN no trajo el ApplicationResponse: no hay con qué armar el AttachedDocument.</summary>
    public static Error ApplicationResponseMissing(string fullNumber) => new(
        "document.application_response_missing",
        $"La DIAN no devolvió el ApplicationResponse del documento {fullNumber}; no se puede regenerar el AttachedDocument.",
        ErrorType.Conflict);

    /// <summary>La solicitud con que se emitió el documento no pidió el correo (<c>sendmail</c>).</summary>
    public static readonly Error EmailNotRequested = new(
        "email.not_requested",
        "La solicitud del documento no pidió el envío por correo (sendmail).",
        ErrorType.Validation);

    /// <summary>El comprador es el consumidor final y la solicitud no trajo <c>email_pos_customer</c>.</summary>
    public static readonly Error EmailFinalConsumer = new(
        "email.final_consumer",
        "El documento es de un consumidor final sin correo (email_pos_customer); no se envía por correo.",
        ErrorType.Validation);

    /// <summary>El comprador no tiene un correo válido.</summary>
    public static readonly Error EmailNoRecipient = new(
        "email.no_recipient",
        "El comprador del documento no tiene un correo electrónico válido.",
        ErrorType.Validation);
}

/// <summary>Busca el documento que pide una ruta de mantenimiento y exige que la DIAN lo haya aceptado.</summary>
internal static class DocumentSelection
{
    public static async Task<Result<DocumentRef>> FindAcceptedAsync(
        IDocumentLookups lookups, Guid companyId, Guid documentId, CancellationToken ct)
    {
        if (await lookups.FindByIdAsync(companyId, documentId, ct) is not { } document)
        {
            return DocumentErrors.NotFound;
        }

        return LegacyDocumentState.Validated.Contains(document.Status) ? document : DocumentErrors.NotAccepted(document.FullNumber);
    }
}
