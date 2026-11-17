using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Notifications;
using ImpiFact.Application.Features.Notifications.ListNotifications;
using ImpiFact.Application.Features.Notifications.MarkRead;
using Microsoft.AspNetCore.Mvc;

namespace ImpiFact.Api.Notifications;

/// <summary>
/// Campana del panel (Arquitectura frontend, sección 9: el panel consulta cada 60 s). Sin equivalente en el PHP. Piden
/// <c>documents:read</c>, el alcance que tienen todos los roles de empresa (propietario, vendedor y cliente) y las API
/// keys de consulta: los avisos son de la empresa activa y marcarlos como leídos no cambia datos fiscales. Una
/// credencial sin empresa (la plataforma) no tiene avisos propios: cada aviso pertenece a una empresa.
/// </summary>
internal sealed class NotificationEndpoints : IEndpointModule
{
    private const string Visibility =
        " Solo se ven los avisos de la empresa activa: los de toda la empresa y, con sesión del panel, los dirigidos al " +
        "usuario. Una API key solo ve los de toda la empresa.";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var notifications = endpoints.MapGroup("/api/v1/notifications").WithTags(ApiTags.Notifications)
            .RequireAuthorization(ApiScopes.DocumentsRead);
        notifications.MapGet("/", List)
            .Produces<NotificationListResponse>()
            .WithOperationId("listNotifications")
            .WithSummary("Listar los avisos de la empresa")
            .WithDescription(
                "Avisos de la campana del panel, del más reciente al más antiguo. `unread=true` trae solo los sin leer y " +
                "`unread=false` solo los leídos; sin `unread`, todos. Pagina por cursor: `limit` (20 si no llega, máximo 100) " +
                "y `cursor` (el `next_cursor` de la página anterior; nulo en la última). `unread_count` es el total sin leer, " +
                "sin importar el filtro ni la página, para el número de la campana. Un `limit` o un `cursor` no válidos " +
                "responden 422." + Visibility);
        notifications.MapPost("/{id:guid}/read", MarkRead)
            .Produces(StatusCodes.Status204NoContent)
            .WithOperationId("markNotificationRead")
            .WithSummary("Marcar un aviso como leído")
            .WithDescription(
                "Marca como leído el aviso `{id}` y responde 204 sin cuerpo. Repetirlo responde lo mismo y no cambia nada. Un " +
                "aviso de otra empresa o dirigido a otro usuario responde 404 (`notification.not_found`), igual que uno que " +
                "no existe." + Visibility);
        notifications.MapPost("/read-all", MarkAllRead)
            .Produces<NotificationsMarkedResponse>()
            .WithOperationId("markAllNotificationsRead")
            .WithSummary("Marcar todos los avisos como leídos")
            .WithDescription(
                "Marca como leídos todos los avisos sin leer que ve la credencial y responde en `marked_count` cuántos " +
                "cambiaron (0 si no había ninguno). Nunca toca los avisos de otra empresa." + Visibility);
    }

    private static async Task<IResult> List(
        [FromQuery(Name = "unread")] bool? unread,
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        IHandler<ListNotificationsQuery, NotificationListResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new ListNotificationsQuery(unread, cursor, limit), ct)).ToHttpResult();

    private static async Task<IResult> MarkRead(
        Guid id, IHandler<MarkNotificationReadCommand, NotificationsMarkedResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new MarkNotificationReadCommand(id), ct)).ToHttpResult(_ => TypedResults.NoContent());

    private static async Task<IResult> MarkAllRead(
        IHandler<MarkAllNotificationsReadCommand, NotificationsMarkedResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new MarkAllNotificationsReadCommand(), ct)).ToHttpResult();
}
