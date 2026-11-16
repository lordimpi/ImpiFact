using System.Text.Json.Serialization;
using ImpiFact.Application.Abstractions.Handlers;

namespace ImpiFact.Application.Features.Companies.UpsertResolution;

/// <summary>
/// Crea o actualiza una resolución de numeración de la DIAN (<c>PUT config/resolution</c>) con los campos de
/// <c>ConfigurationResolutionRequest.php</c>. La resolución queda en el ambiente actual de la familia de su tipo de
/// documento y se identifica, como en el PHP, por tipo de documento, número, prefijo y ambiente.
/// </summary>
public sealed record UpsertResolutionCommand : ICommand
{
    [JsonIgnore]
    public string? Nit { get; init; }

    /// <summary>Si es verdadero, las demás resoluciones del mismo tipo y ambiente quedan inactivas.</summary>
    [JsonPropertyName("delete_all_type_resolutions")]
    public bool? DeleteAllTypeResolutions { get; init; }

    [JsonPropertyName("type_document_id")]
    public int? TypeDocumentId { get; init; }

    [JsonPropertyName("prefix")]
    [JsonConverter(typeof(NumberOrStringConverter))]
    public string? Prefix { get; init; }

    /// <summary>Número de la resolución.</summary>
    [JsonPropertyName("resolution")]
    [JsonConverter(typeof(NumberOrStringConverter))]
    public string? Resolution { get; init; }

    /// <summary>Fecha de la resolución, en formato <c>Y-m-d</c> del PHP (año-mes-día).</summary>
    [JsonPropertyName("resolution_date")]
    public string? ResolutionDate { get; init; }

    /// <summary>Clave técnica (entra en el cálculo del CUFE, el código único de la factura).</summary>
    [JsonPropertyName("technical_key")]
    public string? TechnicalKey { get; init; }

    [JsonPropertyName("from")]
    public long? From { get; init; }

    [JsonPropertyName("to")]
    public long? To { get; init; }

    [JsonPropertyName("date_from")]
    public string? DateFrom { get; init; }

    [JsonPropertyName("date_to")]
    public string? DateTo { get; init; }
}

/// <summary>Respuesta con el mensaje del PHP y la resolución guardada.</summary>
public sealed record ResolutionSavedResponse
{
    public const string SavedMessage = "Resolución creada/actualizada con éxito";

    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("message")]
    public string Message { get; init; } = SavedMessage;

    [JsonPropertyName("resolution")]
    public required ResolutionResponse Resolution { get; init; }
}
