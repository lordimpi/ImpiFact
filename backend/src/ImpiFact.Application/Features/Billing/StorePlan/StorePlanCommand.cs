using System.Text.Json.Serialization;
using ImpiFact.Application.Abstractions.Handlers;

namespace ImpiFact.Application.Features.Billing.StorePlan;

/// <summary>
/// Crea o actualiza un plan por su id (<c>PUT plan</c>, <c>ConfigurationController::storePlan</c> del PHP) con los
/// campos de <c>PlanRequest.php</c>. Es una operación de plataforma. Los números pueden llegar como número o como texto.
/// </summary>
public sealed record StorePlanCommand : ICommand, ICompanyOptional
{
    [JsonPropertyName("id")]
    public int? Id { get; init; }

    /// <summary>Nombre único del plan; si no llega al actualizar, se conserva el que tenía.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("qty_docs_invoice")]
    public long? QtyDocsInvoice { get; init; }

    [JsonPropertyName("qty_docs_payroll")]
    public long? QtyDocsPayroll { get; init; }

    [JsonPropertyName("qty_docs_radian")]
    public long? QtyDocsRadian { get; init; }

    [JsonPropertyName("qty_docs_ds")]
    public long? QtyDocsDs { get; init; }

    /// <summary>1 mensual, 2 anual, 3 paquete (el periodo 0 es solo el plan por defecto del PHP).</summary>
    [JsonPropertyName("period")]
    public int? Period { get; init; }

    /// <summary>Activo o no; verdadero si no llega.</summary>
    [JsonPropertyName("state")]
    public bool? State { get; init; }

    [JsonPropertyName("observations")]
    public string? Observations { get; init; }
}
