using System.Text;
using Dapper;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Application.Features.Notifications;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Notifications;

/// <summary>
/// Bandeja de la campana del panel sobre <c>notifications</c>. Las lecturas van con Dapper (una librería que ejecuta SQL
/// escrito a mano) y llevan <c>company_id</c> en el WHERE, porque los filtros globales de EF Core (Entity Framework) no
/// aplican ahí; las escrituras van con EF Core dentro de la transacción del caso de uso, con el filtro por empresa
/// además de la condición explícita.
/// </summary>
/// <remarks>
/// Usan el índice <c>ix_notifications_company_id_user_id_is_read_created_at</c> (company_id, user_id, is_read,
/// created_at DESC): la empresa y el usuario (nulo o el de la sesión) son iguales en las dos ramas del OR. Los avisos de
/// una empresa son pocos (decenas o cientos), así que el orden final por <c>(created_at, id)</c> no necesita otro índice.
/// SQL de PostgreSQL, el único motor de esta fase.
/// </remarks>
internal sealed class NotificationInbox(IReadConnectionFactory connections, ImpiFactDbContext db, ITenantContext tenant, IClock clock)
    : INotificationInbox
{
    public async Task<IReadOnlyList<NotificationItem>> ListAsync(NotificationListFilter filter, CancellationToken ct)
    {
        var parameters = Audience(filter.Audience);
        parameters.Add("Take", filter.Take);
        var sql = new StringBuilder("""
            select n.id as Id, n.type as Type, n.title as Title, n.body as Body, n.is_read as IsRead, n.created_at as CreatedAt
            from notifications n
            where n.company_id = @CompanyId and (n.user_id is null or n.user_id = @UserId)
            """);
        if (filter.IsRead is { } isRead)
        {
            sql.Append(" and n.is_read = @IsRead");
            parameters.Add("IsRead", isRead);
        }

        if (filter.After is { } after)
        {
            // Siguiente página por clave (created_at, id), del más reciente al más antiguo: lo que va después del último
            // aviso mostrado.
            sql.Append(" and (n.created_at < @AfterCreatedAt or (n.created_at = @AfterCreatedAt and n.id < @AfterId))");
            parameters.Add("AfterCreatedAt", after.CreatedAt.ToUniversalTime());
            parameters.Add("AfterId", after.Id);
        }

        sql.Append(" order by n.created_at desc, n.id desc limit @Take");
        await using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<NotificationRow>(new CommandDefinition(sql.ToString(), parameters, cancellationToken: ct));
        return rows.Select(row => new NotificationItem(
            row.Id, row.Type, row.Title, row.Body, row.IsRead, new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)))).ToList();
    }

    public async Task<long> CountUnreadAsync(NotificationAudience audience, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            select count(*) from notifications n
            where n.company_id = @CompanyId and (n.user_id is null or n.user_id = @UserId) and n.is_read = false
            """,
            Audience(audience),
            cancellationToken: ct));
    }

    public async Task<bool> MarkReadAsync(NotificationAudience audience, Guid notificationId, CancellationToken ct)
    {
        var notification = await VisibleTo(audience).FirstOrDefaultAsync(row => row.Id == notificationId, ct);
        if (notification is null)
        {
            return false;
        }

        // Si ya estaba leído no hay cambio y EF no escribe nada: marcarlo otra vez es idempotente. Si cambia, el
        // interceptor de guardado mueve updated_at con el reloj de la aplicación y sube row_version.
        notification.IsRead = true;
        return true;
    }

    public async Task<int> MarkAllReadAsync(NotificationAudience audience, CancellationToken ct)
    {
        // Un solo UPDATE en lugar de cargar cada fila. No pasa por los interceptores de guardado, así que pone aquí lo
        // mismo que ellos: revisa que la empresa sea la de la solicitud, mueve updated_at y sube row_version.
        TenantWriteCheck.EnsureCanWrite(tenant, audience.CompanyId, nameof(Notification));
        var now = clock.UtcNow;
        return await VisibleTo(audience).Where(row => !row.IsRead).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(row => row.IsRead, true)
                .SetProperty(row => row.UpdatedAt, now)
                .SetProperty(row => row.RowVersion, row => row.RowVersion + 1),
            ct);
    }

    private IQueryable<Notification> VisibleTo(NotificationAudience audience) =>
        db.Set<Notification>().Where(row => row.CompanyId == audience.CompanyId && (row.UserId == null || row.UserId == audience.UserId));

    private static DynamicParameters Audience(NotificationAudience audience) =>
        new(new { audience.CompanyId, audience.UserId });

    // Fila tal como la devuelve el SQL; Npgsql entrega timestamptz como DateTime en UTC.
    private sealed class NotificationRow
    {
        public Guid Id { get; init; }

        public string Type { get; init; } = "";

        public string Title { get; init; } = "";

        public string Body { get; init; } = "";

        public bool IsRead { get; init; }

        public DateTime CreatedAt { get; init; }
    }
}
