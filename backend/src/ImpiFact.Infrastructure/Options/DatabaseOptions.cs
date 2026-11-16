using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>Base de datos (sección «Database» de la configuración; Arquitectura detallada, sección 11).</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Motor de base de datos. En la fase 2 solo existe el proveedor de PostgreSQL.</summary>
    [Required]
    [AllowedValues("Postgres", "SqlServer", "MySql")]
    public string Provider { get; set; } = "";

    /// <summary>
    /// Cadena de conexión. Es un secreto: nunca va en <c>appsettings.json</c>; llega por la variable de entorno
    /// <c>Database__ConnectionString</c> o por el proveedor de secretos de la nube.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = "";
}
