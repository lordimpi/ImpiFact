using ImpiFact.Application.Abstractions.Maintenance;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Application.Features.Maintenance;

public static class MaintenanceServiceCollectionExtensions
{
    /// <summary>
    /// Registra las tareas programadas de Application (<see cref="IScheduledJob"/>): los vigilantes de certificados y de
    /// resoluciones, con ámbito propio en cada corrida. Solo los usa el Worker, desde el programador de Infrastructure.
    /// El correo de sus avisos (<c>SendNoticeEmail</c>) lo registra <c>AddOutboxHandlers</c> con los demás handlers.
    /// </summary>
    public static IServiceCollection AddMaintenanceJobs(this IServiceCollection services)
    {
        services.AddScoped<IScheduledJob, CertificateExpiryWatcher>();
        services.AddScoped<IScheduledJob, ResolutionRangeWatcher>();
        return services;
    }
}
