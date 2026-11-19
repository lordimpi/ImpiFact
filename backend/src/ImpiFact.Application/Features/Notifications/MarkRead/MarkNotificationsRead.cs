using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Notifications.MarkRead;

/// <summary><c>POST /api/v1/notifications/{id}/read</c>: marca un aviso como leído.</summary>
public sealed record MarkNotificationReadCommand(Guid NotificationId) : ICommand;

/// <summary><c>POST /api/v1/notifications/read-all</c>: marca como leídos todos los avisos visibles.</summary>
public sealed record MarkAllNotificationsReadCommand : ICommand;

/// <summary>
/// Idempotente: marcar un aviso ya leído responde bien y no cambia nada. Un aviso de otra empresa o dirigido a otro
/// usuario responde 404, igual que uno que no existe, para no revelar que existe. El decorador de transacción guarda
/// el cambio; la fecha de cambio (<c>updated_at</c>) la pone el interceptor de guardado con el reloj de la aplicación.
/// </summary>
internal sealed class MarkNotificationReadHandler(INotificationInbox inbox, ITenantContext tenant)
    : IHandler<MarkNotificationReadCommand, NotificationsMarkedResponse>
{
    public async Task<Result<NotificationsMarkedResponse>> HandleAsync(MarkNotificationReadCommand request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId
            || !await inbox.MarkReadAsync(new NotificationAudience(companyId, tenant.UserId), request.NotificationId, ct))
        {
            return NotificationErrors.NotFound;
        }

        return new NotificationsMarkedResponse { MarkedCount = 1 };
    }
}

/// <summary>Solo cambia los avisos de la empresa activa visibles para quien llama; repetirlo no cambia nada.</summary>
internal sealed class MarkAllNotificationsReadHandler(INotificationInbox inbox, ITenantContext tenant)
    : IHandler<MarkAllNotificationsReadCommand, NotificationsMarkedResponse>
{
    public async Task<Result<NotificationsMarkedResponse>> HandleAsync(MarkAllNotificationsReadCommand request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
        {
            return NotificationErrors.NotFound;
        }

        var marked = await inbox.MarkAllReadAsync(new NotificationAudience(companyId, tenant.UserId), ct);
        return new NotificationsMarkedResponse { MarkedCount = marked };
    }
}
