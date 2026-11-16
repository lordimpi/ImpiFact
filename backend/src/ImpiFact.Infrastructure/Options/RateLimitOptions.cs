using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Límites de solicitudes por minuto (Arquitectura detallada, secciones 9.1 y 11). Se aplican en memoria en cada
/// instancia de la API; con varias instancias el límite pasa al gateway de la nube (sección 19.1).
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// <summary>Solicitudes por minuto con una misma API key.</summary>
    [Range(1, 100_000)]
    public int PermitPerMinutePerKey { get; set; }

    /// <summary>Solicitudes por minuto desde una misma dirección IP.</summary>
    [Range(1, 100_000)]
    public int PermitPerMinutePerIp { get; set; }

    /// <summary>Intentos de inicio de sesión por minuto desde una misma dirección IP.</summary>
    [Range(1, 10_000)]
    public int LoginPerMinute { get; set; }
}
