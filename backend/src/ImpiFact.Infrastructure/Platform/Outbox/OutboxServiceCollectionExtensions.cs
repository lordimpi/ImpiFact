using ImpiFact.Application;
using ImpiFact.Infrastructure.Email;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Pdf;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Platform.Outbox;

public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// Todos los servicios del Worker: Infrastructure con los motores indicados, Application, los handlers de la
    /// bandeja de salida (<c>AddOutboxHandlers</c>), el procesador de la bandeja de salida y las tareas programadas
    /// diarias (<c>AddScheduledJobs</c>). <c>ImpiFact.Worker</c> solo llama a este método, así las pruebas arman
    /// exactamente la misma composición.
    /// </summary>
    /// <remarks>
    /// Exige al arrancar que un envío a la DIAN y su consulta GetStatus de la regla 90 quepan en el plazo de un handler:
    /// <c>2 × Resilience:TotalSeconds</c> menor que <c>Outbox:HandlerTimeoutSeconds</c>. También exige el servidor de
    /// correo por defecto de la plataforma (sección «Email»).
    /// </remarks>
    public static IServiceCollection AddWorkerServices(
        this IServiceCollection services, IConfiguration configuration, params IDatabaseProvider[] providers)
    {
        services.AddInfrastructure(configuration, providers);
        services.AddApplication();
        services.AddOutboxHandlers();
        services.AddOutboxProcessing(configuration);
        services.AddScheduledJobs(configuration);
        services.AddPdf(configuration);

        // Correo del documento aceptado (SendEmail): MailKit y el servidor de correo de la empresa o de la plataforma.
        services.AddEmail(configuration);
        services.AddOptions<ResilienceOptions>()
            .Validate<IOptions<OutboxOptions>>(
                (resilience, outbox) => 2 * resilience.TotalSeconds < outbox.Value.HandlerTimeoutSeconds,
                $"Two DIAN calls ({ResilienceOptions.SectionName}:{nameof(ResilienceOptions.TotalSeconds)} each) must fit within " +
                $"{OutboxOptions.SectionName}:{nameof(OutboxOptions.HandlerTimeoutSeconds)}.");
        return services;
    }

    /// <summary>
    /// Registra el procesador de la bandeja de salida (outbox: la tabla <c>outbox_messages</c> con el trabajo
    /// pendiente) como servicio en segundo plano, con sus opciones validadas al arrancar (sección «Outbox»). Solo lo
    /// llama el Worker, después de <c>AddInfrastructure</c>, que aporta el SQL del motor; los handlers de cada tipo de
    /// mensaje se registran aparte como <c>IOutboxMessageHandler</c>.
    /// </summary>
    public static IServiceCollection AddOutboxProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<OutboxOptions>(configuration, OutboxOptions.SectionName);
        services.TryAddSingleton(OutboxWorkerId.Create());
        services.TryAddScoped<OutboxQueue>();
        services.TryAddSingleton<OutboxProcessor>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<OutboxProcessor>());
        return services;
    }
}
