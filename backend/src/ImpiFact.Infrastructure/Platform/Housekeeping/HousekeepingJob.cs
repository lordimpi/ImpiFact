using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Maintenance;
using ImpiFact.Domain.Identity;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Platform.Housekeeping;

/// <summary>
/// Tarea diaria <c>Housekeeping</c> (Arquitectura detallada, sección 8): borra los mensajes hechos de la bandeja de
/// salida (outbox: <c>outbox_messages</c>) procesados hace más de <see cref="ScheduledJobsOptions.OutboxRetentionDays"/>
/// días, las llaves de idempotencia (<c>idempotency_keys</c>) vencidas, los tokens de renovación
/// (<c>refresh_tokens</c>) expirados y los tokens para restablecer la contraseña (<c>password_reset_tokens</c>) vencidos.
/// </summary>
/// <remarks>
/// <para>
/// Borra por tandas de <see cref="ScheduledJobsOptions.DeleteBatchSize"/> filas, cada una en su propia sentencia y
/// confirmación, así un atraso grande no deja las tablas bloqueadas mucho tiempo. Los mensajes fallidos o pendientes
/// nunca se borran. Mover <c>request</c>/<c>response</c> antiguos a almacenamiento frío no está en la fase 4: no hay
/// almacenamiento frío configurado.
/// </para>
/// <para>
/// Los tokens de renovación forman cadenas de rotación (<c>replaced_by</c> apunta al token que reemplazó a cada uno,
/// con una llave foránea que impide borrar el token al que otro apunta). Se borran del más antiguo al más nuevo: el
/// que apunta y el apuntado caen en la misma sentencia o el que apunta cae antes. Un token expirado al que apunta un
/// token todavía vivo se deja hasta que ese también expire.
/// </para>
/// <para>
/// Un token para restablecer la contraseña vale 30 minutos y nadie apunta a él con una llave foránea: se borra al vencer,
/// usado, revocado o sin usar. Uno usado o revocado que todavía no vence se deja hasta su vencimiento, que llega pronto. El
/// mensaje <c>SendPasswordResetEmail</c> que siga en la bandeja no envía nada si su token ya no existe.
/// </para>
/// </remarks>
internal sealed class HousekeepingJob(
    ImpiFactDbContext db, IClock clock, IOptions<ScheduledJobsOptions> options, ILogger<HousekeepingJob> logger) : IScheduledJob
{
    public string Name => "Housekeeping";

    public async Task RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var batch = options.Value.DeleteBatchSize;
        var processedBefore = now.AddDays(-options.Value.OutboxRetentionDays);

        var messages = await DeleteInBatchesAsync(
            () => db.Set<OutboxMessage>()
                .Where(message => message.Status == OutboxStatus.Done && message.ProcessedAt < processedBefore)
                .OrderBy(message => message.ProcessedAt)
                .Take(batch)
                .ExecuteDeleteAsync(ct),
            batch);
        var keys = await DeleteInBatchesAsync(
            () => db.Set<IdempotencyKey>().IgnoreQueryFilters([QueryFilters.Tenant])
                .Where(key => key.ExpiresAt < now)
                .OrderBy(key => key.ExpiresAt)
                .Take(batch)
                .ExecuteDeleteAsync(ct),
            batch);
        // MySQL no acepta un DELETE cuya subconsulta lee la misma tabla (error 1093): la tanda se elige primero y se borra
        // por id. Un token vencido nunca gana un antecesor vivo, así que la elección no se vuelve vieja entre las dos
        // sentencias. El DELETE conserva el orden del más antiguo al más nuevo porque MySQL revisa la llave foránea fila
        // por fila (PostgreSQL y SQL Server, al final de la sentencia): el que apunta tiene que caer antes que el apuntado.
        var tokens = await DeleteInBatchesAsync(
            async () =>
            {
                var ids = await db.Set<RefreshToken>()
                    .Where(token => token.ExpiresAt < now
                        && !db.Set<RefreshToken>().Any(newer => newer.ReplacedBy == token.Id && newer.ExpiresAt >= now))
                    .OrderBy(token => token.ExpiresAt)
                    .Take(batch)
                    .Select(token => token.Id)
                    .ToListAsync(ct);
                return ids.Count == 0
                    ? 0
                    : await db.Set<RefreshToken>()
                        .Where(token => ids.Contains(token.Id))
                        .OrderBy(token => token.ExpiresAt)
                        .Take(batch)
                        .ExecuteDeleteAsync(ct);
            },
            batch);
        var resetTokens = await DeleteInBatchesAsync(
            () => db.Set<PasswordResetToken>()
                .Where(token => token.ExpiresAt < now)
                .OrderBy(token => token.ExpiresAt)
                .Take(batch)
                .ExecuteDeleteAsync(ct),
            batch);

        logger.LogInformation(
            "Housekeeping deleted {MessageCount} outbox messages, {KeyCount} idempotency keys, {TokenCount} refresh tokens and " +
            "{ResetTokenCount} password reset tokens.",
            messages, keys, tokens, resetTokens);
    }

    /// <summary>Repite el borrado de una tanda hasta que una tanda viene incompleta.</summary>
    /// <returns>Filas borradas en total.</returns>
    private static async Task<int> DeleteInBatchesAsync(Func<Task<int>> deleteBatch, int batch)
    {
        var total = 0;
        int deleted;
        do
        {
            deleted = await deleteBatch();
            total += deleted;
        }
        while (deleted >= batch);

        return total;
    }
}
