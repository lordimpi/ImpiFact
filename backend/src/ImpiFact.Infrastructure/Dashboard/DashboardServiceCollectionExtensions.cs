using ImpiFact.Application.Features.Dashboard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiFact.Infrastructure.Dashboard;

public static class DashboardServiceCollectionExtensions
{
    /// <summary>Registra las lecturas del tablero del panel.</summary>
    public static IServiceCollection AddDashboardReads(this IServiceCollection services)
    {
        services.TryAddScoped<IDashboardReads, DashboardReads>();
        return services;
    }
}
