using System.Globalization;
using ImpiFact.Application.Abstractions.Documents;

namespace ImpiFact.Application.Features.Documents.Queries;

/// <summary>
/// Fechas de los filtros de consulta. El cliente pide días de calendario en hora Colombia (UTC-5 todo el año, sin
/// horario de verano), que es la fecha que lleva el XML; la base ordena y filtra por el instante en UTC
/// (<c>issued_at</c>), así la consulta usa el índice <c>ix_documents_company_issued</c>.
/// </summary>
public static class ColombiaDates
{
    /// <summary>Formato de fecha que aceptan las rutas y los filtros.</summary>
    public const string Format = "yyyy-MM-dd";

    public static readonly TimeSpan Offset = TimeSpan.FromHours(-5);

    /// <summary>
    /// Lee una fecha <c>yyyy-MM-dd</c>. El último día del calendario (9999-12-31) no se acepta porque el rango
    /// termina al inicio del día siguiente.
    /// </summary>
    public static bool TryParse(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date) && date < DateOnly.MaxValue;

    /// <summary>Del inicio del día <paramref name="from"/> al inicio del día siguiente a <paramref name="to"/>, en UTC.</summary>
    public static IssuedRange Range(DateOnly from, DateOnly to) => new(StartOf(from), StartOf(to.AddDays(1)));

    /// <summary>Instante UTC en que empieza el día en Colombia (las 05:00 UTC).</summary>
    public static DateTimeOffset StartOf(DateOnly day) =>
        new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), Offset).ToUniversalTime();
}

/// <summary>
/// Fechas por defecto de <c>information</c> y <c>total_documents</c>, como el PHP (<c>ResumeController.php</c>): con
/// una sola fecha se consulta ese día; sin fechas, de 1900-01-01 a 2100-01-01.
/// </summary>
public static class LegacyDateRange
{
    public static readonly DateOnly DefaultFrom = new(1900, 1, 1);

    public static readonly DateOnly DefaultTo = new(2100, 1, 1);

    /// <summary>Resuelve el rango; las fechas ya pasaron la validación (vacía cuenta como ausente).</summary>
    public static (DateOnly From, DateOnly To) Resolve(string? desde, string? hasta)
    {
        var hasFrom = ColombiaDates.TryParse(desde, out var from);
        var hasTo = ColombiaDates.TryParse(hasta, out var to);
        return (hasFrom, hasTo) switch
        {
            (true, true) => (from, to),
            (true, false) => (from, from),
            (false, true) => (to, to),
            _ => (DefaultFrom, DefaultTo),
        };
    }
}
