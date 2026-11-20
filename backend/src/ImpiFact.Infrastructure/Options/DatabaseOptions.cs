using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>Base de datos (sección «Database» de la configuración; Arquitectura detallada, sección 11).</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Motor de base de datos, sin valor por defecto. Elige entre los motores que registra el host (hoy solo PostgreSQL;
    /// SQL Server y MySQL llegan con sus proyectos <c>ImpiFact.Infrastructure.&lt;Motor&gt;</c>).
    /// </summary>
    [Required]
    [AllowedValues("Postgres", "SqlServer", "MySql")]
    public string Provider { get; set; } = "";

    /// <summary>
    /// Cadena de conexión. Es un secreto: nunca va en <c>appsettings.json</c>; llega por la variable de entorno
    /// <c>Database__ConnectionString</c> o por el proveedor de secretos de la nube.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Si la API y el Worker revisan al arrancar que la base no tenga migraciones pendientes (por defecto, sí): con
    /// alguna pendiente no arrancan (<see cref="Persistence.PendingMigrationsGuard"/>). Solo lo apagan las pruebas que
    /// levantan la API sobre una base vacía y crean el esquema después, con el host ya arriba.
    /// </summary>
    public bool CheckPendingMigrationsAtStartup { get; set; } = true;
}
