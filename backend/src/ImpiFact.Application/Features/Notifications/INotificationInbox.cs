namespace ImpiFact.Application.Features.Notifications;

/// <summary>
/// Bandeja de avisos del panel (tabla <c>notifications</c>; Arquitectura frontend, sección 9: la campana). Las
/// lecturas van con SQL escrito a mano y las escrituras con EF Core (Entity Framework, el mapeador de objetos a
/// tablas); la implementación vive en Infrastructure. Toda operación recibe la empresa y el usuario a quien se le
/// muestran los avisos: nunca toca los de otra empresa ni los dirigidos a otro usuario.
/// </summary>
/// <remarks>
/// El puerto vive en el módulo y no en <c>Application/Abstractions</c> porque solo lo usan estos casos de uso: los
/// módulos que crean avisos siguen usando <c>ICompanyNotifications</c>.
/// </remarks>
public interface INotificationInbox
{
    /// <summary>
    /// Avisos visibles del más reciente al más antiguo, por clave <c>(created_at, id)</c> y no por desplazamiento: la
    /// página siguiente empieza justo después de <see cref="NotificationListFilter.After"/>.
    /// </summary>
    Task<IReadOnlyList<NotificationItem>> ListAsync(NotificationListFilter filter, CancellationToken ct);

    /// <summary>Cantidad de avisos visibles sin leer, sin importar el filtro ni la página.</summary>
    Task<long> CountUnreadAsync(NotificationAudience audience, CancellationToken ct);

    /// <summary>
    /// Marca el aviso como leído si todavía no lo está; si ya lo está, no cambia nada.
    /// </summary>
    /// <returns>Falso si el aviso no existe o no es visible para <paramref name="audience"/>.</returns>
    Task<bool> MarkReadAsync(NotificationAudience audience, Guid notificationId, CancellationToken ct);

    /// <summary>Marca como leídos todos los avisos visibles sin leer.</summary>
    /// <returns>Cuántos avisos cambiaron.</returns>
    Task<int> MarkAllReadAsync(NotificationAudience audience, CancellationToken ct);
}

/// <summary>
/// A quién se le muestran los avisos: los de toda la empresa (<c>user_id</c> nulo) y, si hay usuario, también los
/// dirigidos a él. Una API key no tiene usuario: solo ve los de toda la empresa.
/// </summary>
public readonly record struct NotificationAudience(Guid CompanyId, Guid? UserId);

/// <summary>Posición de un aviso en el orden del listado: instante de creación y id.</summary>
public readonly record struct NotificationKey(DateTimeOffset CreatedAt, Guid Id);

/// <summary>Filtros del listado.</summary>
/// <param name="IsRead">Solo leídos (<c>true</c>) o solo sin leer (<c>false</c>); nulo para todos.</param>
/// <param name="After">Último aviso de la página anterior; nulo para la primera página.</param>
/// <param name="Take">Máximo de filas que devuelve la consulta.</param>
public sealed record NotificationListFilter(NotificationAudience Audience, bool? IsRead, NotificationKey? After, int Take);

/// <summary>Un aviso tal como se guardó.</summary>
public sealed record NotificationItem(Guid Id, string Type, string Title, string Body, bool IsRead, DateTimeOffset CreatedAt);
