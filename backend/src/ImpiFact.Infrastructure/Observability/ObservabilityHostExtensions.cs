using ImpiFact.Application.Abstractions.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ImpiFact.Infrastructure.Observability;

public static class ObservabilityHostExtensions
{
    /// <summary>
    /// Fuentes de trazas y métricas de los controladores de base de datos: cada consulta a la base, también las de EF
    /// Core. Npgsql (PostgreSQL) las emite por sí mismo con el nombre «Npgsql», así no hace falta su paquete de
    /// OpenTelemetry (que solo agrega esa fuente) ni el de EF Core (que sigue en versión preliminar). Los motores de la
    /// fase 6 agregan la suya.
    /// </summary>
    public static readonly string[] DatabaseDriverSources = ["Npgsql"];

    /// <summary>
    /// Observabilidad de un proceso (Arquitectura detallada, sección 12): registros JSON con Serilog
    /// (<see cref="ImpiFactLogging"/>) y, solo si está la variable estándar <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, trazas y
    /// métricas de OpenTelemetry exportadas por OTLP: las llamadas HTTP salientes (DIAN, Gotenberg, S3), la base de
    /// datos, las métricas de negocio (<see cref="ImpiFactMetrics"/>) y lo que agregue el host. Sin la variable no se
    /// registra nada de OpenTelemetry y no sale nada del proceso.
    /// </summary>
    /// <remarks>
    /// El exportador lee la configuración estándar de OpenTelemetry (<c>OTEL_EXPORTER_OTLP_PROTOCOL</c>,
    /// <c>OTEL_EXPORTER_OTLP_HEADERS</c>…). Las direcciones de las solicitudes salen sin los valores de su consulta
    /// (OpenTelemetry los quita por defecto) y las consultas a la base, sin los valores de sus parámetros.
    /// </remarks>
    /// <param name="serviceName">Nombre del servicio: <c>impifact-api</c> o <c>impifact-worker</c>.</param>
    /// <param name="tracing">Instrumentación propia del host, por ejemplo la de ASP.NET Core.</param>
    /// <param name="metrics">Métricas propias del host.</param>
    public static IHostApplicationBuilder AddImpiFactObservability(
        this IHostApplicationBuilder builder,
        string serviceName,
        Action<TracerProviderBuilder>? tracing = null,
        Action<MeterProviderBuilder>? metrics = null)
    {
        // Sin los proveedores por defecto (la consola de texto de .NET, Debug, EventSource): la consola es la de Serilog.
        builder.Logging.ClearProviders();
        builder.Logging.AddImpiFactSerilog(builder.Configuration, builder.Environment, serviceName);

        if (string.IsNullOrWhiteSpace(builder.Configuration[ImpiFactLogging.OtlpEndpointKey]))
        {
            return builder;
        }

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName)
                .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
            .WithTracing(tracer =>
            {
                tracer.AddHttpClientInstrumentation().AddSource(ImpiFactActivities.SourceName).AddSource(DatabaseDriverSources);
                tracing?.Invoke(tracer);
                tracer.AddOtlpExporter();
            })
            .WithMetrics(meter =>
            {
                meter.AddHttpClientInstrumentation().AddMeter(ImpiFactMetrics.MeterName).AddMeter(DatabaseDriverSources);
                metrics?.Invoke(meter);
                meter.AddOtlpExporter();
            });
        return builder;
    }
}
