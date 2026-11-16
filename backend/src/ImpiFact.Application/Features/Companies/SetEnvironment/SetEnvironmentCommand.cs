using System.Text.Json.Serialization;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Domain.Common;

namespace ImpiFact.Application.Features.Companies.SetEnvironment;

/// <summary>
/// Cambia el ambiente de la DIAN de una o varias familias de documentos (<c>PUT config/environment</c>): 1 producción,
/// 2 habilitación. Acepta los nombres técnicos del PHP y sus alternativas en español
/// (<c>ConfigurationEnvironmentRequest.php</c>); si llegan ambos, gana el técnico, como en el PHP.
/// </summary>
public sealed record SetEnvironmentCommand : ICommand
{
    /// <summary>NIT de la ruta canónica; nulo en la ruta heredada, que no lo trae.</summary>
    [JsonIgnore]
    public string? Nit { get; init; }

    [JsonPropertyName("type_environment_id")]
    public int? TypeEnvironmentId { get; init; }

    [JsonPropertyName("factura_ventas")]
    public int? FacturaVentas { get; init; }

    [JsonPropertyName("payroll_type_environment_id")]
    public int? PayrollTypeEnvironmentId { get; init; }

    [JsonPropertyName("nomina")]
    public int? Nomina { get; init; }

    [JsonPropertyName("eqdocs_type_environment_id")]
    public int? EqdocsTypeEnvironmentId { get; init; }

    [JsonPropertyName("pos")]
    public int? Pos { get; init; }

    [JsonPropertyName("support_document_type_environment_id")]
    public int? SupportDocumentTypeEnvironmentId { get; init; }

    [JsonPropertyName("documentos_soporte")]
    public int? DocumentosSoporte { get; init; }

    [JsonPropertyName("event_type_environment_id")]
    public int? EventTypeEnvironmentId { get; init; }

    [JsonPropertyName("eventos_radian")]
    public int? EventosRadian { get; init; }

    /// <summary>Ambiente pedido para cada familia que trae la solicitud.</summary>
    public IReadOnlyList<(DocumentKind Kind, int TypeEnvironmentId)> Requested()
    {
        var requested = new List<(DocumentKind, int)>();
        Add(DocumentKind.Invoice, TypeEnvironmentId ?? FacturaVentas);
        Add(DocumentKind.Payroll, PayrollTypeEnvironmentId ?? Nomina);
        Add(DocumentKind.EquivalentDocument, EqdocsTypeEnvironmentId ?? Pos);
        Add(DocumentKind.SupportDocument, SupportDocumentTypeEnvironmentId ?? DocumentosSoporte);
        Add(DocumentKind.Events, EventTypeEnvironmentId ?? EventosRadian);
        return requested;

        void Add(DocumentKind kind, int? value)
        {
            if (value is { } environment)
            {
                requested.Add((kind, environment));
            }
        }
    }
}

/// <summary>Respuesta con el mensaje del PHP y los ambientes que quedaron.</summary>
public sealed record EnvironmentsResponse
{
    public const string UpdatedMessage = "Ambiente actualizado con éxito";

    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("message")]
    public string Message { get; init; } = UpdatedMessage;

    [JsonPropertyName("environments")]
    public IReadOnlyList<EnvironmentResponse> Environments { get; init; } = [];
}
