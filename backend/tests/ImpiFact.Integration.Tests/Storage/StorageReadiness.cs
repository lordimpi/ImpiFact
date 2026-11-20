using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ImpiFact.Integration.Tests.Storage;

/// <summary>
/// Corre la prueba de salud del almacenamiento (la misma de <c>/health/ready</c>) con el servicio de salud de .NET, así
/// una excepción del proveedor cuenta como caída igual que en la aplicación.
/// </summary>
internal static class StorageReadiness
{
    public static async Task<HealthReportEntry> CheckAsync(IBlobStorage storage)
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSingleton(storage);
        collection.AddHealthChecks().AddCheck<StorageHealthCheck>("storage", HealthStatus.Unhealthy, [], ReadinessServiceCollectionExtensions.CheckTimeout);
        await using var services = collection.BuildServiceProvider();

        var report = await services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        return report.Entries["storage"];
    }
}
