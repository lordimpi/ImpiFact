using System.ComponentModel.DataAnnotations;
using ImpiFact.Infrastructure.Observability;
using ImpiFact.Infrastructure.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpiFact.Worker.Health;

/// <summary>Sección <c>WorkerHealth</c>: el puerto de las pruebas de salud del Worker.</summary>
public sealed class WorkerHealthOptions
{
    public const string SectionName = "WorkerHealth";

    /// <summary>Puerto HTTP de <c>/health/live</c> y <c>/health/ready</c>; sin valor, el Worker no abre ningún puerto.</summary>
    [Range(1, 65535)]
    public int? Port { get; set; }
}

/// <summary>
/// Pruebas de salud del Worker por HTTP (Arquitectura detallada, sección 12), para las sondas de Docker, Kubernetes y
/// Container Apps, que no saben revisar un proceso sin puerto: un Kestrel mínimo en <see cref="WorkerHealthOptions.Port"/>
/// (en todas las interfaces, porque la sonda llega desde fuera del contenedor) con dos rutas anónimas:
/// <c>/health/live</c> (el proceso responde, sin revisar nada) y <c>/health/ready</c> (las mismas pruebas que la API:
/// base de datos, almacenamiento, secretos y la DIAN degradada según las últimas llamadas del Worker). Solo esas rutas:
/// cualquier otra responde 404.
/// </summary>
public sealed class WorkerHealthServer(
    HealthCheckService health, IOptions<WorkerHealthOptions> options, ILogger<WorkerHealthServer> logger) : IHostedService, IAsyncDisposable
{
    private WebApplication? app;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Port is not { } port)
        {
            return;
        }

        var builder = WebApplication.CreateSlimBuilder();
        // Sus registros propios (cada solicitud) no hacen falta: lo que importa lo registra el Worker.
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenAnyIP(port));
        app = builder.Build();
        app.MapGet("/health/live", () => Results.Text("Healthy"));
        app.MapGet("/health/ready", async (HttpContext context) =>
        {
            var report = await health.CheckHealthAsync(ReadinessServiceCollectionExtensions.IsReadinessCheck, context.RequestAborted);
            context.Response.StatusCode = report.Status == HealthStatus.Unhealthy
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status200OK;
            context.Response.Headers.CacheControl = "no-store, no-cache";
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(ReadinessServiceCollectionExtensions.Serialize(report), context.RequestAborted);
        });
        await app.StartAsync(cancellationToken);
        logger.LogInformation("Worker health endpoints listen on port {Port}: /health/live and /health/ready.", port);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (app is not null)
        {
            await app.StopAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            await app.DisposeAsync();
        }
    }
}

public static class WorkerHealthServiceCollectionExtensions
{
    /// <summary>
    /// Pruebas de salud del Worker (<see cref="ReadinessServiceCollectionExtensions.AddReadinessChecks"/>) y su servidor
    /// HTTP en <c>WorkerHealth:Port</c> (validado al arrancar; sin valor, no abre puerto).
    /// </summary>
    public static IServiceCollection AddWorkerHealthEndpoints(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<WorkerHealthOptions>(configuration, WorkerHealthOptions.SectionName);
        services.AddReadinessChecks();
        services.AddHostedService<WorkerHealthServer>();
        return services;
    }
}
