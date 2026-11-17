using ImpiFact.Application.Abstractions.Maintenance;
using ImpiFact.Application.Features.Maintenance;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Platform.Housekeeping;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiFact.Infrastructure.Platform.Scheduling;

public static class ScheduledJobsServiceCollectionExtensions
{
    /// <summary>
    /// Registra las tareas programadas diarias del Worker (<c>Housekeeping</c> y los vigilantes de Application), sus
    /// puertos, los bloqueos de la base y el servicio en segundo plano que las corre, con las opciones de la sección
    /// «ScheduledJobs» validadas al arrancar. Solo lo llama el Worker, después de <c>AddInfrastructure</c>, que aporta
    /// el SQL del bloqueo de cada motor.
    /// </summary>
    public static IServiceCollection AddScheduledJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<ScheduledJobsOptions>(configuration, ScheduledJobsOptions.SectionName);
        services.AddMaintenanceJobs();
        services.AddScoped<IScheduledJob, HousekeepingJob>();
        services.TryAddScoped<IExpiryWatchReads, EfExpiryWatchReads>();
        services.TryAddScoped<IScheduledNotices, EfScheduledNotices>();
        services.TryAddScoped<ICompanyNoticeReads, EfCompanyNoticeReads>();
        services.TryAddSingleton<ScheduledJobLocks>();
        services.TryAddSingleton<ScheduledJobsService>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<ScheduledJobsService>());
        return services;
    }
}
