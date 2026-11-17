using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Platform.Outbox;

/// <summary>
/// Cola sobre <c>outbox_messages</c> (outbox: la bandeja de salida con el trabajo pendiente del Worker; Arquitectura
/// detallada, sección 8). Toma mensajes con el SQL del motor (<see cref="IOutboxQueueSql"/>) y los cierra con
/// <c>UPDATE</c> directos de EF Core (Entity Framework, el mapeador de objetos a tablas).
/// </summary>
/// <remarks>
/// Cerrar un mensaje (hecho, reintento o fallido) solo funciona si sigue en proceso a nombre de la misma instancia:
/// si su bloqueo venció y otra instancia lo volvió a tomar, la primera ya no puede tocarlo. Usa un ámbito de servicios
/// propio, distinto del que usó el handler, para no guardar por accidente cambios a medias del handler.
/// </remarks>
internal sealed class OutboxQueue(ImpiFactDbContext db, IOutboxQueueSql sql, IClock clock, IOptions<OutboxOptions> options)
{
    /// <summary>Largo máximo de <c>last_error</c>.</summary>
    internal const int LastErrorMaxLength = 2000;

    /// <summary>
    /// Retroceso (backoff: espera cada vez más larga antes de reintentar) después del intento 1, 2, 3, 4 y 5. Si
    /// <see cref="OutboxOptions.MaxAttempts"/> permite más intentos, los siguientes esperan lo mismo que el último.
    /// </summary>
    internal static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2),
    ];

    /// <summary>Toma mensajes vencidos de los tipos indicados a nombre de <paramref name="workerId"/>.</summary>
    public async Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimAsync(
        string workerId, IReadOnlyCollection<string> types, int batchSize, CancellationToken ct)
    {
        if (types.Count == 0)
        {
            return [];
        }

        var now = clock.UtcNow;
        var claim = new OutboxClaim(workerId, types, batchSize, now, now.AddMinutes(options.Value.LockMinutes));
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            return await sql.ClaimAsync(db.Database.GetDbConnection(), claim, ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Marca el mensaje como hecho.</summary>
    /// <returns>Falso si el mensaje ya no está en proceso a nombre de <paramref name="workerId"/>.</returns>
    public async Task<bool> CompleteAsync(string workerId, ClaimedOutboxMessage message, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var updated = await Owned(workerId, message).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(row => row.Status, OutboxStatus.Done)
                .SetProperty(row => row.ProcessedAt, now)
                .SetProperty(row => row.LockedBy, (string?)null)
                .SetProperty(row => row.LockedUntil, (DateTimeOffset?)null),
            ct);
        return updated == 1;
    }

    /// <summary>
    /// Registra un intento fallido. Si quedan intentos y la falla no es permanente
    /// (<see cref="PermanentOutboxFailureException"/>), el mensaje vuelve a pendiente con el retroceso que le toca; si
    /// no, queda fallido y, si es de una empresa, se le deja una notificación en la misma transacción.
    /// </summary>
    public async Task<OutboxFailureOutcome> FailAsync(string workerId, ClaimedOutboxMessage message, Exception error, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var lastError = Truncate($"{error.GetType().Name}: {error.Message}");
        if (!IsFinal(message, error))
        {
            var nextAttemptAt = now + Backoff[Math.Clamp(message.Attempts - 1, 0, Backoff.Length - 1)];
            var rescheduled = await Owned(workerId, message).ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.Status, OutboxStatus.Pending)
                    .SetProperty(row => row.NextAttemptAt, nextAttemptAt)
                    .SetProperty(row => row.LastError, lastError)
                    .SetProperty(row => row.LockedBy, (string?)null)
                    .SetProperty(row => row.LockedUntil, (DateTimeOffset?)null),
                ct);
            return rescheduled == 1 ? OutboxFailureOutcome.Rescheduled : OutboxFailureOutcome.NotOwned;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var failed = await Owned(workerId, message).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(row => row.Status, OutboxStatus.Failed)
                .SetProperty(row => row.ProcessedAt, now)
                .SetProperty(row => row.LastError, lastError)
                .SetProperty(row => row.LockedBy, (string?)null)
                .SetProperty(row => row.LockedUntil, (DateTimeOffset?)null),
            ct);
        if (failed != 1)
        {
            return OutboxFailureOutcome.NotOwned;
        }

        if (message.CompanyId is { } companyId)
        {
            db.Set<Notification>().Add(OutboxNotifications.Failed(companyId, message));
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return OutboxFailureOutcome.Failed;
    }

    /// <summary>
    /// Si esta falla deja el mensaje fallido, sin más intentos: es permanente
    /// (<see cref="PermanentOutboxFailureException"/>) o ya no quedan intentos.
    /// </summary>
    public bool IsFinal(ClaimedOutboxMessage message, Exception error) =>
        error is PermanentOutboxFailureException || message.Attempts >= options.Value.MaxAttempts;

    // El número de intento identifica la toma: si la misma instancia volvió a tomar el mensaje después de que venció
    // su bloqueo, la ejecución anterior ya no puede cerrarlo.
    private IQueryable<OutboxMessage> Owned(string workerId, ClaimedOutboxMessage message) =>
        db.Set<OutboxMessage>().Where(row => row.Id == message.Id
            && row.Status == OutboxStatus.Processing
            && row.LockedBy == workerId
            && row.Attempts == message.Attempts);

    private static string Truncate(string text) => text.Length <= LastErrorMaxLength ? text : text[..LastErrorMaxLength];
}

/// <summary>Resultado de registrar un intento fallido.</summary>
internal enum OutboxFailureOutcome
{
    /// <summary>Volvió a pendiente; se reintenta después del retroceso.</summary>
    Rescheduled,

    /// <summary>Quedó fallido: no hay más intentos.</summary>
    Failed,

    /// <summary>No se tocó: el mensaje ya no estaba en proceso a nombre de esta instancia.</summary>
    NotOwned,
}

/// <summary>
/// Aviso del panel cuando un mensaje queda fallido. Nombra el tipo y el id del mensaje, nunca sus datos ni el error,
/// que pueden traer información sensible; el detalle queda en <c>last_error</c> y en los logs.
/// </summary>
internal static class OutboxNotifications
{
    public const string FailedType = "OutboxMessageFailed";

    public static Notification Failed(Guid companyId, ClaimedOutboxMessage message) => new()
    {
        CompanyId = companyId,
        Type = FailedType,
        Title = "Una tarea en segundo plano falló",
        Body = $"La tarea «{message.Type}» (mensaje {message.Id}) falló después de {message.Attempts} intento(s) y no se reintentará automáticamente.",
    };
}
