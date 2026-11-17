using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Maintenance;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Platform.Scheduling;

/// <summary>Cómo terminó una tarea programada en una corrida.</summary>
public enum ScheduledJobOutcome
{
    Completed,

    /// <summary>Otra instancia del Worker la estaba corriendo; esta instancia no la corrió.</summary>
    Skipped,

    /// <summary>Falló; queda en los logs y se vuelve a intentar en la próxima corrida.</summary>
    Failed,
}

/// <summary>Resultado de una tarea programada en una corrida.</summary>
public sealed record ScheduledJobRun(string Job, ScheduledJobOutcome Outcome);

/// <summary>
/// Servicio en segundo plano del Worker que corre una vez al día, a la hora <see cref="ScheduledJobsOptions.RunAtUtc"/>,
/// cada tarea programada (<see cref="IScheduledJob"/>; Arquitectura detallada, sección 8): <c>Housekeeping</c>,
/// <c>CertificateExpiryWatcher</c> y <c>ResolutionRangeWatcher</c>.
/// </summary>
/// <remarks>
/// Cada tarea corre con su bloqueo de la base (<see cref="ScheduledJobLocks"/>): si otra instancia del Worker ya la
/// está corriendo, esta la salta. Corre en su propio ámbito de servicios como proceso del sistema, sin empresa fija,
/// y su falla se registra sin detener a las demás. Si <see cref="ScheduledJobsOptions.RunOnStartup"/> está activo,
/// también corren al arrancar, para no saltarse un día si el Worker estuvo apagado a la hora programada.
/// </remarks>
internal sealed class ScheduledJobsService(
    IServiceScopeFactory scopes,
    ScheduledJobLocks locks,
    IOptions<ScheduledJobsOptions> options,
    IClock clock,
    ILogger<ScheduledJobsService> logger) : BackgroundService
{
    private IReadOnlyList<string>? jobNames;

    /// <summary>Corre todas las tareas, una después de otra.</summary>
    public async Task<IReadOnlyList<ScheduledJobRun>> RunAllAsync(CancellationToken ct)
    {
        var runs = new List<ScheduledJobRun>();
        foreach (var name in JobNames())
        {
            runs.Add(await RunAsync(name, ct));
        }

        return runs;
    }

    /// <summary>Corre la tarea <paramref name="name"/> si ninguna otra instancia la está corriendo.</summary>
    /// <exception cref="ArgumentException">Si no hay una tarea con ese nombre.</exception>
    public async Task<ScheduledJobRun> RunAsync(string name, CancellationToken ct)
    {
        if (!JobNames().Contains(name, StringComparer.Ordinal))
        {
            throw new ArgumentException($"There is no scheduled job named '{name}'.", nameof(name));
        }

        try
        {
            await using var held = await locks.TryAcquireAsync(name, ct);
            if (held is null)
            {
                logger.LogInformation("Scheduled job {Job} is running in another worker instance; it is skipped here.", name);
                return new ScheduledJobRun(name, ScheduledJobOutcome.Skipped);
            }

            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(companyId: null, userId: null, ActorType.System);
            var job = scope.ServiceProvider.GetServices<IScheduledJob>().Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            logger.LogInformation("Scheduled job {Job} started.", name);
            await job.RunAsync(ct);
            logger.LogInformation("Scheduled job {Job} finished.", name);
            return new ScheduledJobRun(name, ScheduledJobOutcome.Completed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Scheduled job {Job} failed; it runs again on the next daily run.", name);
            return new ScheduledJobRun(name, ScheduledJobOutcome.Failed);
        }
    }

    /// <summary>Cuánto falta, desde <paramref name="now"/>, para la próxima hora <paramref name="runAtUtc"/> en UTC.</summary>
    internal static TimeSpan DelayUntilNextRun(DateTimeOffset now, TimeOnly runAtUtc)
    {
        var utc = now.UtcDateTime;
        var today = utc.Date + runAtUtc.ToTimeSpan();
        return (today > utc ? today : today.AddDays(1)) - utc;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Fuera del try: dos tareas con el mismo nombre es un error de composición y detiene el Worker al arrancar.
        logger.LogInformation(
            "Scheduled jobs {Jobs} run every day at {RunAtUtc} UTC.", string.Join(", ", JobNames()), options.Value.RunAtUtc);
        try
        {
            await Task.Yield();
            if (options.Value.RunOnStartup)
            {
                await RunAllAsync(stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(DelayUntilNextRun(clock.UtcNow, options.Value.RunAtUtc), stoppingToken);
                await RunAllAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // El Worker se apaga.
        }
    }

    /// <summary>Nombres de las tareas registradas; se calculan una vez, porque los registros no cambian en ejecución.</summary>
    /// <exception cref="InvalidOperationException">Si dos tareas tienen el mismo nombre.</exception>
    private IReadOnlyList<string> JobNames()
    {
        if (jobNames is not null)
        {
            return jobNames;
        }

        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(companyId: null, userId: null, ActorType.System);
        var names = scope.ServiceProvider.GetServices<IScheduledJob>().Select(job => job.Name).ToList();
        var duplicated = names.GroupBy(name => name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        if (duplicated.Count > 0)
        {
            throw new InvalidOperationException($"More than one scheduled job is named: {string.Join(", ", duplicated)}.");
        }

        jobNames = names;
        return jobNames;
    }
}
