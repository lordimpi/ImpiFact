using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiFact.Infrastructure.Platform;

public static class PlatformServiceCollectionExtensions
{
    /// <summary>Registra los adaptadores del módulo Platform: la bandeja de salida (outbox) y los avisos del panel.</summary>
    public static IServiceCollection AddPlatform(this IServiceCollection services)
    {
        services.TryAddScoped<IOutbox, EfOutbox>();
        services.TryAddScoped<ICompanyNotifications, EfCompanyNotifications>();
        return services;
    }
}
