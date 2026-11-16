using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// API keys (llaves de acceso de las integraciones de cada empresa). El encabezado Bearer que empieza con este
/// prefijo se trata como API key y no como JWT (Arquitectura detallada, sección 9.2).
/// </summary>
public sealed class ApiKeyOptions
{
    public const string SectionName = "ApiKey";

    /// <summary>Prefijo de toda API key: letras minúsculas terminadas en guion bajo, por ejemplo «apk_».</summary>
    [Required]
    [RegularExpression("^[a-z]+_$")]
    // Valor por defecto aquí y no solo en appsettings: la API y el Migrator deben reconocer las mismas llaves.
    public string Prefix { get; set; } = "apk_";
}
