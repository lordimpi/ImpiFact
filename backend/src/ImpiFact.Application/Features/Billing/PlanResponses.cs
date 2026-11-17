using System.Globalization;
using System.Text.Json.Serialization;
using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;

namespace ImpiFact.Application.Features.Billing;

/// <summary>
/// Un plan con los campos del modelo <c>TypePlan</c> del PHP: una cantidad por familia (<c>qty_docs_*</c>), el periodo
/// (0 por defecto, 1 mensual, 2 anual, 3 paquete), el estado y las fechas de Laravel (el framework del PHP) en UTC.
/// </summary>
public sealed record PlanResponse
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("qty_docs_invoice")]
    public long QtyDocsInvoice { get; init; }

    [JsonPropertyName("qty_docs_payroll")]
    public long QtyDocsPayroll { get; init; }

    /// <summary>Eventos RADIAN (registro de la factura electrónica como título valor).</summary>
    [JsonPropertyName("qty_docs_radian")]
    public long QtyDocsRadian { get; init; }

    /// <summary>Documento soporte.</summary>
    [JsonPropertyName("qty_docs_ds")]
    public long QtyDocsDs { get; init; }

    [JsonPropertyName("period")]
    public int Period { get; init; }

    [JsonPropertyName("state")]
    public bool State { get; init; }

    [JsonPropertyName("observations")]
    public string? Observations { get; init; }

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = "";

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = "";

    /// <summary>Familias de documentos de cada columna <c>qty_docs_*</c> del PHP.</summary>
    public static readonly IReadOnlyList<(DocumentKind Kind, string Field)> QuotaFields =
    [
        (DocumentKind.Invoice, "qty_docs_invoice"),
        (DocumentKind.Payroll, "qty_docs_payroll"),
        (DocumentKind.Events, "qty_docs_radian"),
        (DocumentKind.SupportDocument, "qty_docs_ds"),
    ];

    public static PlanResponse From(PlanWithQuotas plan) =>
        From(plan.Plan, plan.Quotas, plan.Plan.CreatedAt, plan.Plan.UpdatedAt);

    /// <param name="createdAt">Fecha de creación; se pasa aparte porque la de un plan nuevo la pone el guardado.</param>
    public static PlanResponse From(Plan plan, IReadOnlyDictionary<DocumentKind, long> quotas, DateTimeOffset createdAt, DateTimeOffset updatedAt) => new()
    {
        Id = plan.Id,
        Name = plan.Name,
        QtyDocsInvoice = quotas.GetValueOrDefault(DocumentKind.Invoice),
        QtyDocsPayroll = quotas.GetValueOrDefault(DocumentKind.Payroll),
        QtyDocsRadian = quotas.GetValueOrDefault(DocumentKind.Events),
        QtyDocsDs = quotas.GetValueOrDefault(DocumentKind.SupportDocument),
        Period = (int)plan.Period,
        State = plan.IsActive,
        Observations = plan.Observations,
        CreatedAt = LaravelTimestamp(createdAt),
        UpdatedAt = LaravelTimestamp(updatedAt),
    };

    /// <summary>Fecha como la serializa Laravel 10: UTC con microsegundos, por ejemplo <c>2026-01-15T10:00:00.000000Z</c>.</summary>
    public static string LaravelTimestamp(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
}

/// <summary>Respuesta de <c>PUT plan</c>, con el mensaje del PHP.</summary>
public sealed record PlanSavedResponse
{
    public const string SavedMessage = "Plan creado/actualizado con éxito";

    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("message")]
    public string Message { get; init; } = SavedMessage;

    [JsonPropertyName("plan")]
    public PlanResponse Plan { get; init; } = new();
}

/// <summary>
/// Respuesta de <c>GET plan/query/{id?}</c>, con el mensaje del PHP. <c>data</c> es un plan cuando se pide uno por id y
/// una lista cuando no.
/// </summary>
public sealed record PlansResponse
{
    public const string FoundMessage = "Plan/es existe/n, informacion del/los plan/es:";

    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("message")]
    public string Message { get; init; } = FoundMessage;

    [JsonPropertyName("data")]
    public object Data { get; init; } = Array.Empty<PlanResponse>();
}

/// <summary>
/// Respuesta de <c>GET plan/queryusersbyplan/{id}</c>: las empresas con el plan contratado activo, en el campo
/// <c>user</c> del PHP. El PHP devolvía la fila completa de cada empresa; aquí solo sus datos de identificación.
/// </summary>
public sealed record PlanCompaniesResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";

    [JsonPropertyName("user")]
    public IReadOnlyList<PlanCompanyResponse> User { get; init; } = [];
}

/// <summary>Una empresa con el plan contratado activo.</summary>
public sealed record PlanCompanyResponse
{
    /// <summary>NIT (Número de Identificación Tributaria) sin dígito de verificación.</summary>
    [JsonPropertyName("identification_number")]
    public string IdentificationNumber { get; init; } = "";

    [JsonPropertyName("dv")]
    public string Dv { get; init; } = "";

    /// <summary>Razón social.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("email")]
    public string Email { get; init; } = "";

    /// <summary>Empresa activa, como <c>companies.state</c> del PHP.</summary>
    [JsonPropertyName("state")]
    public bool State { get; init; }

    [JsonPropertyName("type_plan_id")]
    public int TypePlanId { get; init; }

    [JsonPropertyName("start_plan_date")]
    public string StartPlanDate { get; init; } = "";
}

/// <summary>
/// Respuesta de <c>GET plan/infoplanuser</c> con los campos del PHP. El PHP tenía cuatro planes por empresa (factura,
/// nómina, RADIAN y documento soporte, en <c>plan</c> a <c>plan4</c>); en ImpiFact un plan contratado trae la cantidad
/// de cada familia, así que los cuatro grupos muestran el mismo plan y <c>docs_left_*</c> sale de su cantidad por
/// familia menos lo usado.
/// </summary>
public sealed record CurrentPlanResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";

    [JsonPropertyName("plan")]
    public PlanResponse? Plan { get; init; }

    [JsonPropertyName("period")]
    public string Period { get; init; } = "";

    [JsonPropertyName("start_plan_date")]
    public string? StartPlanDate { get; init; }

    [JsonPropertyName("renovation_date")]
    public string? RenovationDate { get; init; }

    [JsonPropertyName("plan2")]
    public PlanResponse? Plan2 { get; init; }

    [JsonPropertyName("period2")]
    public string Period2 { get; init; } = "";

    [JsonPropertyName("start_plan_date2")]
    public string? StartPlanDate2 { get; init; }

    [JsonPropertyName("renovation_date2")]
    public string? RenovationDate2 { get; init; }

    [JsonPropertyName("plan3")]
    public PlanResponse? Plan3 { get; init; }

    [JsonPropertyName("period3")]
    public string Period3 { get; init; } = "";

    [JsonPropertyName("start_plan_date3")]
    public string? StartPlanDate3 { get; init; }

    [JsonPropertyName("renovation_date3")]
    public string? RenovationDate3 { get; init; }

    [JsonPropertyName("plan4")]
    public PlanResponse? Plan4 { get; init; }

    [JsonPropertyName("period4")]
    public string Period4 { get; init; } = "";

    [JsonPropertyName("start_plan_date4")]
    public string? StartPlanDate4 { get; init; }

    [JsonPropertyName("renovation_date4")]
    public string? RenovationDate4 { get; init; }

    [JsonPropertyName("docs_left_invoice")]
    public long DocsLeftInvoice { get; init; }

    [JsonPropertyName("docs_left_payroll")]
    public long DocsLeftPayroll { get; init; }

    [JsonPropertyName("docs_left_radian")]
    public long DocsLeftRadian { get; init; }

    [JsonPropertyName("docs_left_ds")]
    public long DocsLeftDs { get; init; }
}

/// <summary>Respuesta de la asignación de un plan a una empresa (ruta propia de ImpiFact, sin equivalente en el PHP).</summary>
public sealed record PlanAssignedResponse
{
    public const string AssignedMessage = "Plan asignado con éxito";

    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("message")]
    public string Message { get; init; } = AssignedMessage;

    [JsonPropertyName("plan")]
    public PlanResponse Plan { get; init; } = new();

    [JsonPropertyName("start_plan_date")]
    public string StartPlanDate { get; init; } = "";
}

/// <summary>Fechas del plan contratado como las mostraba el PHP: hora de Colombia en formato <c>Y-m-d H:i:s</c>.</summary>
internal static class PlanDates
{
    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    public static string Format(DateTimeOffset instant) =>
        instant.ToOffset(ColombiaOffset).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Nombre del periodo que mostraba <c>infoPlanUser</c> del PHP.</summary>
    public static string PeriodName(PlanPeriod? period) => period switch
    {
        PlanPeriod.Monthly => "MENSUAL",
        PlanPeriod.Yearly => "ANUAL",
        PlanPeriod.Package => "PAQUETE",
        _ => "SIN LIMITES - NO PLAN",
    };
}
