using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Infrastructure.Observability;

public static class ObservabilityHostExtensions
{
    /// <summary>
    /// Observabilidad de un proceso (Arquitectura detallada, sección 12): registros JSON con Serilog
    /// (<see cref="ImpiFactLogging"/>).
    /// </summary>
    /// <param name="serviceName">Nombre del servicio: <c>impifact-api</c> o <c>impifact-worker</c>.</param>
    public static IHostApplicationBuilder AddImpiFactObservability(this IHostApplicationBuilder builder, string serviceName)
    {
        // Sin los proveedores por defecto (la consola de texto de .NET, Debug, EventSource): la consola es la de Serilog.
        builder.Logging.ClearProviders();
        builder.Logging.AddImpiFactSerilog(builder.Configuration, builder.Environment, serviceName);
        return builder;
    }
}
