using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Application.Features.Documents.Queries;
using ImpiFact.Application.Features.Documents.Queries.DianStatus;

namespace ImpiFact.Application.Features.Documents.Maintenance;

/// <summary>
/// Cómo se elige el documento en las rutas de mantenimiento: por id (rutas canónicas) o, en las heredadas, por su
/// CUFE o CUDE (códigos únicos de factura y de documento electrónico) o por su prefijo y número, en ese orden, como el
/// PHP.
/// </summary>
public interface IDocumentSelector
{
    Guid? DocumentId { get; }

    string? Cufe { get; }

    string? Prefix { get; }

    LegacyScalar? Number { get; }
}

/// <summary>
/// <c>POST regeneratepdf[/{prefix}/{number}/{cufe}]</c> del PHP (carpeta 10 del Postman): vuelve a generar el PDF de un
/// documento aceptado. El PHP lo generaba en la solicitud (con el cuerpo de una factura, en la variante sin ruta); aquí
/// se encola <c>RegeneratePdf</c> y el Worker guarda una revisión nueva del PDF con los datos guardados del documento.
/// Del cuerpo solo se leen <c>cufe</c>, <c>prefix</c> y <c>number</c>.
/// </summary>
public sealed record RegeneratePdfCommand : IDocumentSelector, ICommand
{
    [JsonIgnore]
    public Guid? DocumentId { get; init; }

    [JsonPropertyName("cufe")]
    public string? Cufe { get; init; }

    [JsonPropertyName("prefix")]
    public string? Prefix { get; init; }

    [JsonPropertyName("number")]
    public LegacyScalar? Number { get; init; }
}

/// <summary>
/// <c>POST regenerate-attached-document</c> (carpeta 05 del Postman; no está en el PHP Community): vuelve a armar y
/// firmar el AttachedDocument (contenedor electrónico) de un documento aceptado con el ApplicationResponse (respuesta
/// firmada de la DIAN) guardado. Se encola <c>RegenerateAttachedDocument</c>; el Worker guarda una revisión nueva.
/// </summary>
public sealed record RegenerateAttachedDocumentCommand : IDocumentSelector, ICommand
{
    [JsonIgnore]
    public Guid? DocumentId { get; init; }

    [JsonPropertyName("cufe")]
    public string? Cufe { get; init; }

    [JsonPropertyName("prefix")]
    public string? Prefix { get; init; }

    [JsonPropertyName("number")]
    public LegacyScalar? Number { get; init; }
}

/// <summary>
/// <c>POST send-email</c> del PHP (carpeta 05 del Postman): vuelve a enviar el correo de un documento aceptado. Se
/// encola <c>SendEmail</c> con los destinatarios de la solicitud con que se emitió, aunque el correo ya se hubiera
/// enviado. Como el PHP, <c>prefix</c> y <c>number</c> son obligatorios en la ruta heredada. <c>alternate_email</c>,
/// <c>email_cc_list</c>, <c>only_send_to_cc_list</c>, <c>base64graphicrepresentation</c> y
/// <c>showacceptrejectbuttons</c> se aceptan por el contrato y se ignoran.
/// </summary>
public sealed record SendDocumentEmailCommand : IDocumentSelector, ICommand
{
    [JsonIgnore]
    public Guid? DocumentId { get; init; }

    [JsonIgnore]
    public string? Cufe => null;

    [JsonPropertyName("prefix")]
    public string? Prefix { get; init; }

    [JsonPropertyName("number")]
    public LegacyScalar? Number { get; init; }
}

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
        IDocumentLookups lookups, Guid companyId, IDocumentSelector selector, CancellationToken ct)
    {
        DocumentRef? document = null;
        if (selector.DocumentId is { } id)
        {
            document = await lookups.FindByIdAsync(companyId, id, ct);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(selector.Cufe))
            {
                document = await lookups.FindByCufeAsync(companyId, selector.Cufe.Trim(), ct);
            }

            if (document is null && selector.Prefix is { } prefix && NumberOf(selector) is { } number)
            {
                document = await lookups.FindByNumberAsync(companyId, prefix.Trim(), number, ct);
            }
        }

        if (document is null)
        {
            return DocumentErrors.NotFound;
        }

        return LegacyDocumentState.Validated.Contains(document.Status) ? document : DocumentErrors.NotAccepted(document.FullNumber);
    }

    /// <summary>El número como entero; nulo si no llegó o no es un entero positivo.</summary>
    public static long? NumberOf(IDocumentSelector selector) =>
        selector.Number is { Kind: LegacyScalarKind.String or LegacyScalarKind.Number } scalar
        && long.TryParse(scalar.Text.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
}

/// <summary>
/// Reglas comunes: en las rutas heredadas debe llegar el <c>cufe</c> o el <c>prefix</c> con el <c>number</c>, y el
/// número debe ser un entero.
/// </summary>
internal abstract class DocumentSelectorValidator<T> : AbstractValidator<T>
    where T : IDocumentSelector
{
    protected DocumentSelectorValidator(bool requiresNumber)
    {
        When(selector => selector.DocumentId is null, () =>
        {
            if (requiresNumber)
            {
                RuleFor(selector => selector.Prefix)
                    .Must(prefix => !string.IsNullOrWhiteSpace(prefix)).WithMessage(QueryMessages.Required("prefix"))
                    .OverridePropertyName("prefix");
                RuleFor(selector => selector.Number)
                    .Must(number => !LegacyScalar.IsMissing(number)).WithMessage(QueryMessages.Required("number"))
                    .OverridePropertyName("number");
            }
            else
            {
                RuleFor(selector => selector)
                    .Must(selector => !string.IsNullOrWhiteSpace(selector.Cufe)
                        || (!string.IsNullOrWhiteSpace(selector.Prefix) && !LegacyScalar.IsMissing(selector.Number)))
                    .WithMessage("Debe enviar el cufe o el prefix y el number del documento.")
                    .OverridePropertyName("cufe");
            }

            RuleFor(selector => selector)
                .Must(selector => LegacyScalar.IsMissing(selector.Number) || DocumentSelection.NumberOf(selector) is not null)
                .WithMessage(QueryMessages.Integer("number"))
                .OverridePropertyName("number");
            RuleFor(selector => selector.Cufe)
                .MaximumLength(96).WithMessage(QueryMessages.MaxLength("cufe", 96))
                .OverridePropertyName("cufe");
        });
    }
}

internal sealed class RegeneratePdfCommandValidator() : DocumentSelectorValidator<RegeneratePdfCommand>(requiresNumber: false);

internal sealed class RegenerateAttachedDocumentCommandValidator() : DocumentSelectorValidator<RegenerateAttachedDocumentCommand>(requiresNumber: false);

internal sealed class SendDocumentEmailCommandValidator() : DocumentSelectorValidator<SendDocumentEmailCommand>(requiresNumber: true);
