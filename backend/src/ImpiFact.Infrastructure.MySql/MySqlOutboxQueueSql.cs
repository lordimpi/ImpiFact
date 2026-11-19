using System.Data;
using System.Data.Common;
using System.Text.Json.Nodes;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Platform.Outbox;

namespace ImpiFact.Infrastructure.MySql;

/// <summary>
/// Toma de la bandeja de salida (outbox: la tabla <c>outbox_messages</c> con el trabajo pendiente del Worker) en MySQL
/// 8. MySQL no tiene <c>UPDATE … RETURNING</c> ni acepta <c>FOR UPDATE</c> en la subconsulta de un <c>UPDATE</c>, así
/// que la toma es una transacción corta: el <c>SELECT … FOR UPDATE SKIP LOCKED</c> (ver <see cref="SelectDueAsync"/>) bloquea las filas
/// elegidas y salta las que otra transacción ya bloqueó (dos instancias que consultan a la vez reciben mensajes
/// distintos sin esperarse), el <c>UPDATE</c> las marca en proceso y el último <c>SELECT</c> las devuelve. Los bloqueos
/// duran hasta el <c>COMMIT</c>, así ninguna otra instancia puede tomar la misma fila entre el primer paso y el último.
/// </summary>
internal sealed class MySqlOutboxQueueSql : IOutboxQueueSql
{
    public async Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimAsync(DbConnection connection, OutboxClaim claim, CancellationToken ct)
    {
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var ids = await SelectDueAsync(connection, transaction, claim, ct);
        if (ids.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return [];
        }

        await MarkProcessingAsync(connection, transaction, claim, ids, ct);
        var claimed = await ReadClaimedAsync(connection, transaction, ids, ct);
        await transaction.CommitAsync(ct);
        return claimed;
    }

    /// <summary>
    /// Elige y bloquea hasta <see cref="OutboxClaim.BatchSize"/> mensajes: primero los que quedaron en proceso con el
    /// bloqueo vencido (la instancia que los tenía se cayó) y después los pendientes, cada grupo del más antiguo al más
    /// nuevo.
    /// </summary>
    /// <remarks>
    /// Son dos consultas y no una con <c>OR</c> porque InnoDB bloquea cada fila que lee: con el <c>OR</c> no puede recorrer
    /// el índice <c>(status, next_attempt_at)</c> en orden, lee y bloquea todas las filas vencidas para ordenarlas y luego
    /// devuelve solo las primeras; las demás quedan bloqueadas hasta el <c>COMMIT</c> y otra instancia que consulta a la
    /// vez las salta y se queda sin mensajes. Recorriendo el índice en orden (<c>FORCE INDEX</c>), cada consulta lee y
    /// bloquea solo las filas que devuelve, más las de otros tipos que encuentra en el camino, que en <c>READ COMMITTED</c>
    /// se sueltan en cuanto no cumplen la condición.
    /// </remarks>
    private static async Task<List<Guid>> SelectDueAsync(DbConnection connection, DbTransaction transaction, OutboxClaim claim, CancellationToken ct)
    {
        var ids = await LockAsync(
            connection, transaction, claim, OutboxStatus.Processing, "locked_until < @now", claim.BatchSize, ct);
        if (ids.Count < claim.BatchSize)
        {
            ids.AddRange(await LockAsync(
                connection, transaction, claim, OutboxStatus.Pending, "next_attempt_at <= @now", claim.BatchSize - ids.Count, ct));
        }

        return ids;
    }

    private static async Task<List<Guid>> LockAsync(
        DbConnection connection, DbTransaction transaction, OutboxClaim claim, OutboxStatus status, string due, int limit, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var types = InList(command, "type", claim.Types.Cast<object>());
        command.CommandText =
            $"""
            SELECT id
            FROM outbox_messages FORCE INDEX (ix_outbox_messages_status_next_attempt_at)
            WHERE status = @status AND {due} AND type IN ({types})
            ORDER BY next_attempt_at
            LIMIT @limit
            FOR UPDATE SKIP LOCKED
            """;
        MySqlParameters.Add(command, "status", (short)status);
        MySqlParameters.Add(command, "now", claim.Now);
        MySqlParameters.Add(command, "limit", limit);

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private static async Task MarkProcessingAsync(
        DbConnection connection, DbTransaction transaction, OutboxClaim claim, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var list = InList(command, "id", ids.Cast<object>());
        command.CommandText =
            $"""
            UPDATE outbox_messages
            SET status = @processing, locked_by = @worker_id, locked_until = @locked_until, attempts = attempts + 1
            WHERE id IN ({list})
            """;
        MySqlParameters.Add(command, "processing", (short)OutboxStatus.Processing);
        MySqlParameters.Add(command, "worker_id", claim.WorkerId);
        MySqlParameters.Add(command, "locked_until", claim.LockedUntil);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyList<ClaimedOutboxMessage>> ReadClaimedAsync(
        DbConnection connection, DbTransaction transaction, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var list = InList(command, "id", ids.Cast<object>());
        command.CommandText =
            $"""
            SELECT id, company_id, type, payload, attempts, next_attempt_at
            FROM outbox_messages
            WHERE id IN ({list})
            ORDER BY next_attempt_at
            """;

        var claimed = new List<ClaimedOutboxMessage>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            claimed.Add(new ClaimedOutboxMessage(
                reader.GetGuid(0),
                await reader.IsDBNullAsync(1, ct) ? null : reader.GetGuid(1),
                reader.GetString(2),
                JsonNode.Parse(reader.GetString(3)) ?? throw new InvalidOperationException("An outbox message has a null payload."),
                reader.GetInt32(4),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc))));
        }

        return claimed;
    }

    /// <summary>Un parámetro por valor (<c>@type0, @type1, …</c>) para un <c>IN (…)</c>; devuelve la lista para el SQL.</summary>
    private static string InList(DbCommand command, string prefix, IEnumerable<object> values)
    {
        var names = new List<string>();
        foreach (var value in values)
        {
            var name = $"{prefix}{names.Count}";
            MySqlParameters.Add(command, name, value);
            names.Add("@" + name);
        }

        return string.Join(", ", names);
    }
}
