using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiFact.Infrastructure.Platform.Outbox;

public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registra el procesador de la bandeja de salida (outbox: la tabla <c>outbox_messages</c> con el trabajo
    /// pendiente) como servicio en segundo plano, con sus opciones validadas al arrancar (sección «Outbox»). Solo lo
    /// llama el Worker, después de <c>AddInfrastructure</c>, que aporta el SQL del motor; los handlers de cada tipo de
    /// mensaje se registran aparte como <c>IOutboxMessageHandler</c>.
    /// </summary>
    public static IServiceCollection AddOutboxProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<OutboxOptions>(configuration, OutboxOptions.SectionName);
        services.TryAddScoped<OutboxQueue>();
        return services;
    }
}
