using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ImpiFact.Infrastructure.Observability;

/// <summary>
/// Pruebas de salud de la API y del Worker (Arquitectura detallada, sección 12). <c>/health/live</c> no corre ninguna:
/// solo dice que el proceso responde. <c>/health/ready</c> corre las de la etiqueta <see cref="ReadyTag"/>: base de
/// datos, almacenamiento y proveedor de secretos (caídos: 503) y la DIAN, que a lo sumo queda degradada (200).
/// </summary>
public static class ReadinessServiceCollectionExtensions
{
    public const string ReadyTag = "ready";

    /// <summary>Plazo de cada prueba: una dependencia que no contesta a tiempo cuenta como caída.</summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static IServiceCollection AddReadinessChecks(this IServiceCollection services)
    {
        services.TryAddSingleton<DianCallMonitor>();
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", HealthStatus.Unhealthy, [ReadyTag], CheckTimeout)
            .AddCheck<StorageHealthCheck>("storage", HealthStatus.Unhealthy, [ReadyTag], CheckTimeout)
            .AddCheck<SecretsHealthCheck>("secrets", HealthStatus.Unhealthy, [ReadyTag], CheckTimeout)
            .AddCheck<DianHealthCheck>("dian", HealthStatus.Degraded, [ReadyTag], CheckTimeout);
        return services;
    }

    /// <summary>Si una prueba es de <c>/health/ready</c>.</summary>
    public static bool IsReadinessCheck(HealthCheckRegistration registration) => registration.Tags.Contains(ReadyTag);

    /// <summary>
    /// El cuerpo de <c>/health/ready</c>: el estado general y el de cada prueba, por ejemplo
    /// <c>{"status":"Unhealthy","checks":{"database":"Unhealthy","storage":"Healthy",…}}</c>. Nunca lleva descripciones,
    /// excepciones ni datos de las dependencias: es una ruta anónima.
    /// </summary>
    public static string Serialize(HealthReport report) =>
        JsonSerializer.Serialize(
            new
            {
                Status = report.Status.ToString(),
                Checks = report.Entries.ToDictionary(entry => entry.Key, entry => entry.Value.Status.ToString()),
            },
            Json);
}
