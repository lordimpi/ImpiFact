using ImpiFact.Application.Abstractions.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiFact.Infrastructure.Platform;

public static class PlatformServiceCollectionExtensions
{
    /// <summary>Registra los adaptadores del módulo Platform: por ahora, la bandeja de salida (outbox).</summary>
    public static IServiceCollection AddPlatform(this IServiceCollection services)
    {
        services.TryAddScoped<IOutbox, EfOutbox>();
        return services;
    }
}
