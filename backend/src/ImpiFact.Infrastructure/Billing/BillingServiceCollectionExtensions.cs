using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiFact.Infrastructure.Billing;

public static class BillingServiceCollectionExtensions
{
    /// <summary>
    /// Registra los adaptadores del módulo Billing (planes y consumo): el almacén de planes y los consumos pendientes,
    /// que la unidad de trabajo ejecuta al guardar (<see cref="IPendingWrites"/>). El SQL del consumo
    /// (<see cref="IPlanUsageSql"/>) lo registra el motor de base de datos elegido.
    /// </summary>
    public static IServiceCollection AddBilling(this IServiceCollection services)
    {
        services.TryAddScoped<IPlanStore, EfPlanStore>();
        if (services.Any(descriptor => descriptor.ServiceType == typeof(PendingPlanUsage)))
        {
            return services;
        }

        services.AddScoped<PendingPlanUsage>();
        services.AddScoped<IPendingWrites>(serviceProvider => serviceProvider.GetRequiredService<PendingPlanUsage>());
        return services;
    }
}
