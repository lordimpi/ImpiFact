namespace ImpiFact.Application.Features.Companies;

/// <summary>
/// Días que le quedan a un certificado (<c>certificate_days_left</c> del PHP): días entre la fecha de hoy y la de
/// vencimiento, ambas en la hora de Colombia y sin la hora del día. Es la misma cuenta que hace el revisor de
/// certificados al subirlo; aquí se usa para los certificados ya guardados.
/// </summary>
internal static class CertificateDays
{
    /// <summary>Colombia está en UTC-5 todo el año, sin horario de verano.</summary>
    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    public static int Left(DateTimeOffset notAfter, DateTimeOffset now) =>
        ColombiaDate(notAfter).DayNumber - ColombiaDate(now).DayNumber;

    /// <summary>Fecha del calendario en Colombia de un instante.</summary>
    public static DateOnly ColombiaDate(DateTimeOffset instant) => DateOnly.FromDateTime(instant.ToOffset(ColombiaOffset).DateTime);
}
