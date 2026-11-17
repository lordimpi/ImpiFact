using ImpiFact.Application.Features.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiFact.Infrastructure.Notifications;

public static class NotificationsServiceCollectionExtensions
{
    /// <summary>Registra la bandeja de la campana del panel: listar avisos y marcarlos como leídos.</summary>
    public static IServiceCollection AddNotificationInbox(this IServiceCollection services)
    {
        services.TryAddScoped<INotificationInbox, NotificationInbox>();
        return services;
    }
}
