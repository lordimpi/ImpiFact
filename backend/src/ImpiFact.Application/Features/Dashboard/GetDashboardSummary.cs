using System.Globalization;
using System.Text.Json.Serialization;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Maintenance;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Common;

namespace ImpiFact.Application.Features.Dashboard;

/// <summary><c>GET /api/v1/dashboard/summary</c>: lo que muestra el tablero del panel para la empresa activa.</summary>
public sealed record GetDashboardSummaryQuery;

/// <summary>
/// Arma el tablero con cuatro lecturas fijas, sin importar cuántos documentos o resoluciones tenga la empresa (ninguna
/// consulta por fila): un solo conteo agrupado de los documentos de los últimos 12 meses, el plan vigente, el
/// certificado activo y las resoluciones activas. Los meses y los días se cuentan en el calendario de Colombia (UTC-5
/// todo el año, sin horario de verano), el de las fechas del XML.
/// </summary>
internal sealed class GetDashboardSummaryHandler(IDashboardReads reads, ITenantContext tenant, IClock clock)
    : IHandler<GetDashboardSummaryQuery, DashboardSummaryResponse>
{
    /// <summary>Meses que muestra la serie, incluido el actual.</summary>
    public const int Months = 12;

    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    public async Task<Result<DashboardSummaryResponse>> HandleAsync(GetDashboardSummaryQuery request, CancellationToken ct)
    {
        var companyId = tenant.CompanyId ?? throw new InvalidOperationException("The tenant decorator guarantees a company.");
        var now = clock.UtcNow;
        var today = ColombiaDate(now);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        var firstMonth = currentMonth.AddMonths(1 - Months);

        var counts = await reads.CountDocumentsAsync(companyId, StartOf(firstMonth), StartOf(currentMonth.AddMonths(1)), ct);
        var plan = await reads.FindCurrentPlanAsync(companyId, ct);
        var certificate = await reads.FindActiveCertificateAsync(companyId, ct);
        var resolutions = await reads.ListActiveResolutionsAsync(companyId, ct);

        var currentKey = MonthKey(currentMonth);
        var current = counts.Where(row => row.Month == currentKey).ToList();
        return new DashboardSummaryResponse
        {
            GeneratedAt = now,
            CurrentMonth = new DashboardCurrentMonthResponse
            {
                Month = currentKey,
                Total = current.Sum(row => row.Count),
                ByStatus = current.GroupBy(row => row.Status)
                    .Select(group => new DashboardStatusCountResponse { Status = group.Key, Count = group.Sum(row => row.Count) })
                    .OrderByDescending(item => item.Count).ThenBy(item => item.Status, StringComparer.Ordinal)
                    .ToList(),
            },
            Months = Enumerable.Range(0, Months).Select(offset => MonthOf(firstMonth.AddMonths(offset), counts)).ToList(),
            Plan = plan is null ? null : PlanOf(plan, now),
            Certificate = certificate is null ? null : new DashboardCertificateResponse
            {
                Name = certificate.Name,
                NotAfter = certificate.NotAfter,
                DaysLeft = ColombiaDate(certificate.NotAfter).DayNumber - today.DayNumber,
                Expired = certificate.NotAfter <= now,
            },
            Resolutions = resolutions
                .Select(resolution => (Resolution: resolution, Alert: ResolutionAlertRule.Evaluate(
                    resolution.RangeFrom, resolution.RangeTo, resolution.NextNumber, resolution.ValidTo, today)))
                .Where(item => item.Alert.Any)
                .OrderBy(item => item.Resolution.DocumentTypeId).ThenBy(item => item.Resolution.Prefix, StringComparer.Ordinal)
                .ThenBy(item => item.Resolution.Id)
                .Select(item => ResolutionOf(item.Resolution, item.Alert))
                .ToList(),
        };
    }

    private static DashboardMonthResponse MonthOf(DateOnly month, IReadOnlyList<DocumentCountRow> counts)
    {
        var key = MonthKey(month);
        var rows = counts.Where(row => row.Month == key).ToList();
        long Of(string? kind) => rows.Where(row => row.Kind == kind).Sum(row => row.Count);
        return new DashboardMonthResponse
        {
            Month = key,
            Invoice = Of("invoice"),
            CreditNote = Of("credit_note"),
            DebitNote = Of("debit_note"),
            Other = Of(null),
            Total = rows.Sum(row => row.Count),
        };
    }

    private static DashboardPlanResponse PlanOf(DashboardPlan plan, DateTimeOffset now) => new()
    {
        Name = plan.Name,
        Period = plan.Period,
        IsActive = plan.IsActive,
        StartsAt = plan.StartsAt,
        PeriodEnd = plan.PeriodEnd,
        Expired = plan.PeriodEnd is { } end && now >= end,
        Quotas = plan.Quotas.Select(quota => new DashboardQuotaResponse
        {
            Kind = KindName(quota.Kind),
            Used = quota.Used,
            Limit = quota.Limit,
            Remaining = quota.Limit is { } limit ? Math.Max(0, limit - quota.Used) : null,
        }).ToList(),
    };

    private static DashboardResolutionResponse ResolutionOf(DashboardResolution resolution, ResolutionAlert alert) => new()
    {
        Id = resolution.Id,
        DocumentTypeId = resolution.DocumentTypeId,
        Kind = resolution.Kind,
        Prefix = resolution.Prefix,
        ResolutionNumber = resolution.ResolutionNumber,
        RangeFrom = resolution.RangeFrom,
        RangeTo = resolution.RangeTo,
        NextNumber = resolution.NextNumber,
        RemainingNumbers = alert.Remaining,
        ValidTo = resolution.ValidTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DaysLeft = alert.DaysLeft,
        NumbersLow = alert.NumbersLow,
        Expiring = alert.Expiring,
        Expired = alert.Expired,
    };

    /// <summary>Nombre de la familia en el plan, en snake_case como el resto del JSON.</summary>
    private static string KindName(DocumentKind kind) => kind switch
    {
        DocumentKind.Invoice => "invoice",
        DocumentKind.Payroll => "payroll",
        DocumentKind.EquivalentDocument => "equivalent_document",
        DocumentKind.SupportDocument => "support_document",
        DocumentKind.Events => "events",
        _ => kind.ToString(),
    };

    private static string MonthKey(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    // La misma cuenta de fechas de Colombia que repiten otros módulos (ColombiaCalendar, CertificateDays): un módulo no
    // puede usar las piezas internas de otro.
    private static DateOnly ColombiaDate(DateTimeOffset instant) => DateOnly.FromDateTime(instant.ToOffset(ColombiaOffset).DateTime);

    private static DateTimeOffset StartOf(DateOnly day) => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), ColombiaOffset).ToUniversalTime();
}

/// <summary>Resumen del tablero. Los nombres JSON van en snake_case como el resto de la API.</summary>
public sealed record DashboardSummaryResponse
{
    /// <summary>Instante (UTC) en que se armó el resumen.</summary>
    [JsonPropertyName("generated_at")]
    public DateTimeOffset GeneratedAt { get; init; }

    [JsonPropertyName("current_month")]
    public DashboardCurrentMonthResponse CurrentMonth { get; init; } = new();

    /// <summary>Los últimos 12 meses, del más antiguo al actual, también los que no tienen documentos.</summary>
    [JsonPropertyName("months")]
    public IReadOnlyList<DashboardMonthResponse> Months { get; init; } = [];

    /// <summary>Plan vigente; nulo si la empresa no tiene plan contratado (sin límites).</summary>
    [JsonPropertyName("plan")]
    public DashboardPlanResponse? Plan { get; init; }

    /// <summary>Certificado de firma activo; nulo si la empresa no tiene uno.</summary>
    [JsonPropertyName("certificate")]
    public DashboardCertificateResponse? Certificate { get; init; }

    /// <summary>
    /// Resoluciones activas por agotarse: menos del 10 % del rango o menos de 30 días de vigencia (las mismas reglas de
    /// los avisos de la campana). Vacía si ninguna lo está.
    /// </summary>
    [JsonPropertyName("resolutions")]
    public IReadOnlyList<DashboardResolutionResponse> Resolutions { get; init; } = [];
}

/// <summary>Documentos emitidos en el mes actual por estado.</summary>
public sealed record DashboardCurrentMonthResponse
{
    /// <summary>Mes del calendario de Colombia, <c>yyyy-MM</c>.</summary>
    [JsonPropertyName("month")]
    public string Month { get; init; } = "";

    [JsonPropertyName("total")]
    public long Total { get; init; }

    /// <summary>Solo los estados con documentos, de más a menos.</summary>
    [JsonPropertyName("by_status")]
    public IReadOnlyList<DashboardStatusCountResponse> ByStatus { get; init; } = [];
}

public sealed record DashboardStatusCountResponse
{
    /// <summary>Nombre del estado (<c>Accepted</c>, <c>Rejected</c>…), como en el listado.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("count")]
    public long Count { get; init; }
}

/// <summary>Documentos emitidos en un mes por familia, en todos los estados.</summary>
public sealed record DashboardMonthResponse
{
    /// <summary>Mes del calendario de Colombia, <c>yyyy-MM</c>.</summary>
    [JsonPropertyName("month")]
    public string Month { get; init; } = "";

    [JsonPropertyName("invoice")]
    public long Invoice { get; init; }

    [JsonPropertyName("credit_note")]
    public long CreditNote { get; init; }

    [JsonPropertyName("debit_note")]
    public long DebitNote { get; init; }

    /// <summary>Los demás tipos de documento.</summary>
    [JsonPropertyName("other")]
    public long Other { get; init; }

    [JsonPropertyName("total")]
    public long Total { get; init; }
}

/// <summary>Plan vigente y su consumo.</summary>
public sealed record DashboardPlanResponse
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary><c>unlimited</c> (sin límites), <c>monthly</c>, <c>yearly</c> o <c>package</c>.</summary>
    [JsonPropertyName("period")]
    public string Period { get; init; } = "";

    /// <summary>Falso si la plataforma desactivó el plan: no deja emitir.</summary>
    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; }

    [JsonPropertyName("starts_at")]
    public DateTimeOffset StartsAt { get; init; }

    /// <summary>Fin del periodo (fecha de renovación); nulo si el plan no vence.</summary>
    [JsonPropertyName("period_end")]
    public DateTimeOffset? PeriodEnd { get; init; }

    /// <summary>Verdadero si el periodo ya terminó: no deja emitir hasta renovar.</summary>
    [JsonPropertyName("expired")]
    public bool Expired { get; init; }

    /// <summary>Familias que el plan incluye o que ya tienen consumo.</summary>
    [JsonPropertyName("quotas")]
    public IReadOnlyList<DashboardQuotaResponse> Quotas { get; init; } = [];
}

public sealed record DashboardQuotaResponse
{
    /// <summary><c>invoice</c>, <c>payroll</c>, <c>equivalent_document</c>, <c>support_document</c> o <c>events</c> (RADIAN).</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "";

    /// <summary>Documentos aceptados por la DIAN en este plan contratado.</summary>
    [JsonPropertyName("used")]
    public long Used { get; init; }

    /// <summary>Documentos que incluye el plan; nulo si no tiene límites.</summary>
    [JsonPropertyName("limit")]
    public long? Limit { get; init; }

    /// <summary>Documentos que quedan (nunca negativo); nulo si no tiene límites.</summary>
    [JsonPropertyName("remaining")]
    public long? Remaining { get; init; }
}

/// <summary>Certificado de firma activo.</summary>
public sealed record DashboardCertificateResponse
{
    /// <summary>Nombre del archivo con que se subió.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("not_after")]
    public DateTimeOffset NotAfter { get; init; }

    /// <summary>Días del calendario de Colombia hasta el vencimiento; negativo si ya venció.</summary>
    [JsonPropertyName("days_left")]
    public int DaysLeft { get; init; }

    [JsonPropertyName("expired")]
    public bool Expired { get; init; }
}

/// <summary>Resolución de numeración por agotarse.</summary>
public sealed record DashboardResolutionResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("document_type_id")]
    public int DocumentTypeId { get; init; }

    /// <summary><c>invoice</c>, <c>credit_note</c> o <c>debit_note</c>; nulo para los demás tipos.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("prefix")]
    public string? Prefix { get; init; }

    [JsonPropertyName("resolution_number")]
    public string? ResolutionNumber { get; init; }

    [JsonPropertyName("range_from")]
    public long RangeFrom { get; init; }

    [JsonPropertyName("range_to")]
    public long RangeTo { get; init; }

    /// <summary>Próximo número del consecutivo; nulo si todavía no se emitió ningún documento con ella.</summary>
    [JsonPropertyName("next_number")]
    public long? NextNumber { get; init; }

    [JsonPropertyName("remaining_numbers")]
    public long RemainingNumbers { get; init; }

    /// <summary>Fin de la vigencia, <c>yyyy-MM-dd</c>; nulo si la resolución no tiene.</summary>
    [JsonPropertyName("valid_to")]
    public string? ValidTo { get; init; }

    /// <summary>Días del calendario de Colombia hasta el fin de la vigencia; negativo si ya pasó, nulo sin vigencia.</summary>
    [JsonPropertyName("days_left")]
    public int? DaysLeft { get; init; }

    /// <summary>Quedan menos del 10 % de los números.</summary>
    [JsonPropertyName("numbers_low")]
    public bool NumbersLow { get; init; }

    /// <summary>Faltan menos de 30 días para el fin de la vigencia (o ya pasó).</summary>
    [JsonPropertyName("expiring")]
    public bool Expiring { get; init; }

    [JsonPropertyName("expired")]
    public bool Expired { get; init; }
}
