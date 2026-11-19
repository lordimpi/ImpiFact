using ImpiFact.Application.Abstractions.Dian;
using ImpiFact.Application.Abstractions.Email;
using ImpiFact.Application.Abstractions.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiFact.Infrastructure.Observability;

public static class ObservabilityServiceCollectionExtensions
{
    /// <summary>
    /// Métricas de negocio (<see cref="ImpiFactMetrics"/>) y la medición de las llamadas a la DIAN (un decorador de
    /// Scrutor sobre <see cref="IDianWebService"/>). <c>AddInfrastructure</c> la llama después de registrar el cliente de
    /// la DIAN.
    /// </summary>
    internal static IServiceCollection AddBusinessMetrics(this IServiceCollection services)
    {
        services.AddMetrics();
        services.TryAddSingleton<ImpiFactMetrics>();
        services.TryAddSingleton<DianCallMonitor>();
        services.Decorate<IDianWebService>((inner, provider) => new InstrumentedDianWebService(
            inner, provider.GetRequiredService<ImpiFactMetrics>(), provider.GetRequiredService<DianCallMonitor>()));
        return services;
    }

    /// <summary>Cuenta los correos enviados y fallidos (<c>impifact.emails.*</c>); la llama el Worker después de <c>AddEmail</c>.</summary>
    public static IServiceCollection AddEmailMetrics(this IServiceCollection services) =>
        services.Decorate<IEmailSender>((inner, provider) =>
            new InstrumentedEmailSender(inner, provider.GetRequiredService<ImpiFactMetrics>()));
}
