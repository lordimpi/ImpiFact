using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Notifications.ListNotifications;

/// <summary>
/// <c>GET /api/v1/notifications</c>: avisos de la empresa activa para la campana del panel, del más reciente al más
/// antiguo, con la cantidad sin leer.
/// </summary>
/// <param name="Unread"><c>true</c> solo los sin leer, <c>false</c> solo los leídos; nulo para todos.</param>
/// <param name="Cursor">El <c>next_cursor</c> de la página anterior.</param>
/// <param name="Limit">Avisos por página; 20 si no se indica, máximo 100.</param>
public sealed record ListNotificationsQuery(bool? Unread, string? Cursor, int? Limit);

/// <summary>
/// Pide una fila más de las que muestra: si llega, hay página siguiente y su cursor es la posición del último aviso
/// mostrado. El conteo sin leer es otra consulta, sobre el mismo índice <c>(company_id, user_id, is_read,
/// created_at)</c>. El decorador de empresa ya cortó las solicitudes sin empresa activa.
/// </summary>
internal sealed class ListNotificationsHandler(INotificationInbox inbox, ITenantContext tenant)
    : IHandler<ListNotificationsQuery, NotificationListResponse>
{
    public async Task<Result<NotificationListResponse>> HandleAsync(ListNotificationsQuery request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
        {
            return NotificationErrors.NotFound;
        }

        var audience = new NotificationAudience(companyId, tenant.UserId);
        var limit = request.Limit ?? ListNotificationsQueryValidator.DefaultLimit;
        NotificationKey? after = NotificationCursor.TryDecode(request.Cursor, out var key) ? key : null;
        bool? isRead = request.Unread is { } unread ? !unread : null;

        var rows = await inbox.ListAsync(new NotificationListFilter(audience, isRead, after, limit + 1), ct);
        var unreadCount = await inbox.CountUnreadAsync(audience, ct);

        var page = rows.Take(limit).ToList();
        var last = page.LastOrDefault();
        return new NotificationListResponse
        {
            Items = page.Select(Response).ToList(),
            NextCursor = rows.Count > limit && last is not null ? NotificationCursor.Encode(new NotificationKey(last.CreatedAt, last.Id)) : null,
            UnreadCount = unreadCount,
        };
    }

    private static NotificationResponse Response(NotificationItem item) => new()
    {
        Id = item.Id,
        Type = item.Type,
        Title = item.Title,
        Body = item.Body,
        IsRead = item.IsRead,
        CreatedAt = item.CreatedAt,
    };
}

internal sealed class ListNotificationsQueryValidator : AbstractValidator<ListNotificationsQuery>
{
    public const int DefaultLimit = 20;

    public const int MaxLimit = 100;

    public ListNotificationsQueryValidator()
    {
        RuleFor(query => query.Limit)
            .GreaterThanOrEqualTo(1).WithMessage(NotificationMessages.MinNumber("limit", 1))
            .LessThanOrEqualTo(MaxLimit).WithMessage(NotificationMessages.MaxNumber("limit", MaxLimit))
            .OverridePropertyName("limit");
        RuleFor(query => query.Cursor)
            .Must(cursor => cursor is null || NotificationCursor.TryDecode(cursor, out _))
            .OverridePropertyName("cursor")
            .WithMessage(NotificationMessages.Invalid("cursor"));
    }
}
