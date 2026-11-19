using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace ImpiFact.Infrastructure.Persistence;

/// <summary>
/// Un motor de base de datos (PostgreSQL, SQL Server o MySQL). Cada proyecto <c>ImpiFact.Infrastructure.&lt;Motor&gt;</c>
/// trae uno; <c>AddInfrastructure</c> usa el que nombra <c>Database:Provider</c> en la configuración.
/// </summary>
public interface IDatabaseProvider
{
    /// <summary>Nombre que se escribe en <c>Database:Provider</c>, por ejemplo «Postgres».</summary>
    string Name { get; }

    /// <summary>Registra los servicios propios del motor, como la fábrica de conexiones de lectura.</summary>
    void AddServices(IServiceCollection services);

    /// <summary>Conecta el contexto de EF Core (Entity Framework) con el motor.</summary>
    void ConfigureDbContext(IServiceProvider services, DbContextOptionsBuilder options);

    /// <summary>Si el error de guardado es la violación de un índice único (cada motor la informa con su propio código).</summary>
    bool IsUniqueViolation(DbUpdateException exception);

    /// <summary>
    /// Si el error es una falla pasajera de la base (servidor caído, red, tiempo de espera al conectar): quien la recibe
    /// responde «no disponible» o sigue sin la base en lugar de fallar del todo. Por defecto, lo que el controlador
    /// marca con <see cref="DbException.IsTransient"/> (Npgsql lo hace; SqlClient no, y su proveedor lo decide por el
    /// número de error).
    /// </summary>
    bool IsTransient(DbException exception) => exception.IsTransient;

    /// <summary>
    /// Trazas del controlador de la base: un tramo por comando, también los de EF Core. Cada motor las agrega a su modo
    /// (Npgsql tiene su propia fuente; SqlClient necesita la instrumentación de OpenTelemetry). Solo se usan si el host
    /// arma OpenTelemetry (<c>AddImpiFactObservability</c> con <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>). Por defecto, ninguna.
    /// </summary>
    void ConfigureTracing(TracerProviderBuilder tracing)
    {
    }

    /// <summary>Métricas del controlador de la base, con la misma regla que <see cref="ConfigureTracing"/>. Por defecto, ninguna.</summary>
    void ConfigureMetrics(MeterProviderBuilder metrics)
    {
    }
}
