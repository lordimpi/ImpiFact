using System.ComponentModel.DataAnnotations;
using System.Net;
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

/// <summary>Sección <c>WorkerHealth</c>: la dirección y el puerto de las pruebas de salud del Worker.</summary>
public sealed class WorkerHealthOptions : IValidatableObject
{
    public const string SectionName = "WorkerHealth";

    /// <summary>La dirección por defecto: solo loopback, para que el puerto no quede abierto a la red local.</summary>
    public const string LoopbackHost = "127.0.0.1";

    /// <summary>Puerto HTTP de <c>/health/live</c> y <c>/health/ready</c>; sin valor, el Worker no abre ningún puerto.</summary>
    [Range(1, 65535)]
    public int? Port { get; set; }

    /// <summary>
    /// Dirección IP en la que escucha ese puerto: <see cref="LoopbackHost"/> si no se fija. En un contenedor va
    /// <c>0.0.0.0</c> (<c>WorkerHealth__Host</c>), porque la sonda llega desde fuera del contenedor.
    /// </summary>
    public string? Host { get; set; } = LoopbackHost;

    /// <summary>
    /// La dirección de <see cref="Host"/>; loopback si está vacía. Es un método y no una propiedad porque la validación
    /// de las opciones recorre las propiedades públicas antes de rechazar un <see cref="Host"/> inválido.
    /// </summary>
    public IPAddress ListenAddress() => string.IsNullOrWhiteSpace(Host) ? IPAddress.Loopback : IPAddress.Parse(Host.Trim());

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Host) && !IPAddress.TryParse(Host.Trim(), out _))
        {
            yield return new ValidationResult(
                $"{nameof(Host)} must be an IP address such as 127.0.0.1 or 0.0.0.0, not '{Host}'.", [nameof(Host)]);
        }
    }
}

/// <summary>
/// Pruebas de salud del Worker por HTTP (Arquitectura detallada, sección 12), para las sondas de Docker, Kubernetes y
/// Container Apps, que no saben revisar un proceso sin puerto: un Kestrel mínimo en <see cref="WorkerHealthOptions.Host"/>
/// (loopback por defecto; los contenedores fijan <c>0.0.0.0</c>, porque la sonda llega desde fuera del contenedor) y
/// <see cref="WorkerHealthOptions.Port"/> con dos rutas anónimas:
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
        var address = options.Value.ListenAddress();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(address, port));
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
        logger.LogInformation(
            "Worker health endpoints listen on {Address}:{Port}: /health/live and /health/ready.", address.ToString(), port);
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
    /// HTTP en <c>WorkerHealth:Host</c> y <c>WorkerHealth:Port</c> (validados al arrancar; sin puerto, no abre ninguno).
    /// </summary>
    public static IServiceCollection AddWorkerHealthEndpoints(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<WorkerHealthOptions>(configuration, WorkerHealthOptions.SectionName);
        services.AddReadinessChecks();
        services.AddHostedService<WorkerHealthServer>();
        return services;
    }
}
