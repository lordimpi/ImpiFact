namespace ImpiFact.Application.Features.Maintenance;

/// <summary>
/// Fechas del calendario de Colombia para los vigilantes. Es la misma cuenta de <c>CertificateDays</c> del módulo
/// Companies (días entre dos fechas de Colombia, sin la hora del día), repetida aquí porque un módulo no puede usar
/// las piezas internas de otro (regla 4 de Arquitectura detallada, sección 1).
/// </summary>
internal static class ColombiaCalendar
{
    /// <summary>Colombia está en UTC-5 todo el año, sin horario de verano.</summary>
    private static readonly TimeSpan Offset = TimeSpan.FromHours(-5);

    /// <summary>Fecha del calendario en Colombia de un instante.</summary>
    public static DateOnly DateOf(DateTimeOffset instant) => DateOnly.FromDateTime(instant.ToOffset(Offset).DateTime);

    /// <summary>Días del calendario de Colombia que faltan desde <paramref name="now"/> hasta <paramref name="until"/>.</summary>
    public static int DaysBetween(DateTimeOffset now, DateTimeOffset until) => DateOf(until).DayNumber - DateOf(now).DayNumber;
}
