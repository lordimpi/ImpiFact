using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using Serilog.Formatting;
using Serilog.Formatting.Compact;
using Serilog.Formatting.Display;
using Serilog.Sinks.OpenTelemetry;

namespace ImpiFact.Infrastructure.Observability;

/// <summary>
/// Registros (logs) de la API y del Worker con Serilog (Arquitectura detallada, sección 12): una línea JSON por evento
/// en la consola (o texto legible en Development) y, si está <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, los mismos eventos por
/// OTLP. Todos pasan antes por <see cref="SensitiveDataRedactor"/>.
/// </summary>
/// <remarks>
/// Serilog entra como un proveedor más de Microsoft.Extensions.Logging, no en su lugar: los niveles siguen saliendo de
/// <c>Logging:LogLevel</c> (por categoría, como hasta ahora, y por variables de entorno como
/// <c>Logging__LogLevel__Default</c>), los ámbitos (scopes) como el <c>CorrelationId</c> llegan como propiedades y los
/// demás proveedores que se agreguen (las pruebas agregan uno) siguen recibiendo todo.
/// </remarks>
public static class ImpiFactLogging
{
    /// <summary>Clave del formato de la consola: <c>json</c> o <c>text</c>.</summary>
    public const string FormatKey = "Observability:LogFormat";

    /// <summary>Variable de entorno estándar de OpenTelemetry con la dirección del colector OTLP.</summary>
    public const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Variable de entorno estándar con el protocolo OTLP: <c>grpc</c> (por defecto) o <c>http/protobuf</c>.</summary>
    public const string OtlpProtocolKey = "OTEL_EXPORTER_OTLP_PROTOCOL";

    /// <summary>
    /// Agrega Serilog como proveedor de registros. Los hosts quitan antes los proveedores por defecto
    /// (<see cref="ObservabilityHostExtensions.AddImpiFactObservability"/>).
    /// </summary>
    /// <param name="serviceName">Nombre del servicio en OTLP: <c>impifact-api</c> o <c>impifact-worker</c>.</param>
    public static ILoggingBuilder AddImpiFactSerilog(
        this ILoggingBuilder logging, IConfiguration configuration, IHostEnvironment environment, string serviceName)
    {
        logging.Services.TryAddSingleton(LogOutput.Console);
        logging.Services.AddSingleton<ILoggerProvider>(services =>
            new SerilogLoggerProvider(CreateLogger(configuration, environment, serviceName, services.GetRequiredService<LogOutput>()), dispose: true));
        return logging;
    }

    /// <summary>Si la consola escribe JSON: lo dice <see cref="FormatKey"/>; si falta, JSON salvo en Development.</summary>
    public static bool WritesJson(IConfiguration configuration, IHostEnvironment environment) =>
        configuration[FormatKey]?.Trim().ToLowerInvariant() switch
        {
            "json" => true,
            "text" => false,
            null or "" => !environment.IsDevelopment(),
            var other => throw new InvalidOperationException($"{FormatKey} must be 'json' or 'text', not '{other}'."),
        };

    private static Serilog.Core.Logger CreateLogger(
        IConfiguration configuration, IHostEnvironment environment, string serviceName, LogOutput output)
    {
        // El nivel mínimo de Serilog queda abierto: el filtro es el de Microsoft.Extensions.Logging (Logging:LogLevel).
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.With(new SensitiveDataRedactor());

        ITextFormatter formatter = WritesJson(configuration, environment)
            ? new RenderedCompactJsonFormatter()
            : new MessageTemplateTextFormatter(
                "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
        logger = output.Writer is null
            ? logger.WriteTo.Console(formatter)
            : logger.WriteTo.Sink(new TextWriterSink(formatter, output.Writer));

        if (configuration[OtlpEndpointKey] is { Length: > 0 } endpoint)
        {
            var http = string.Equals(configuration[OtlpProtocolKey], "http/protobuf", StringComparison.OrdinalIgnoreCase);
            logger = logger.WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = http ? endpoint.TrimEnd('/') + "/v1/logs" : endpoint;
                options.Protocol = http ? OtlpProtocol.HttpProtobuf : OtlpProtocol.Grpc;
                options.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = serviceName,
                    ["deployment.environment.name"] = environment.EnvironmentName,
                };
            });
        }

        return logger.CreateLogger();
    }
}

/// <summary>Adónde escribe la consola de los registros; las pruebas lo cambian por un texto en memoria.</summary>
/// <param name="Writer">Nulo para la consola del proceso.</param>
internal sealed record LogOutput(TextWriter? Writer)
{
    public static readonly LogOutput Console = new((TextWriter?)null);
}

/// <summary>Escribe cada evento con el formato dado en un <see cref="TextWriter"/>, de a uno.</summary>
internal sealed class TextWriterSink(ITextFormatter formatter, TextWriter writer) : Serilog.Core.ILogEventSink
{
    private readonly Lock gate = new();

    public void Emit(Serilog.Events.LogEvent logEvent)
    {
        lock (gate)
        {
            formatter.Format(logEvent, writer);
            writer.Flush();
        }
    }
}
