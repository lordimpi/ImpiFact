using System.Data;
using System.Data.Common;
using System.Text.Json.Nodes;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Platform.Outbox;

namespace ImpiFact.Infrastructure.MySql;

/// <summary>
/// Toma de la bandeja de salida (outbox: la tabla <c>outbox_messages</c> con el trabajo pendiente del Worker) en MySQL
/// 8. MySQL no tiene <c>UPDATE … RETURNING</c> ni acepta <c>FOR UPDATE</c> en la subconsulta de un <c>UPDATE</c>, así
/// que la toma son tres sentencias en una transacción corta: el <c>SELECT … FOR UPDATE SKIP LOCKED</c> bloquea las filas
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

    private static async Task<List<Guid>> SelectDueAsync(DbConnection connection, DbTransaction transaction, OutboxClaim claim, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var types = InList(command, "type", claim.Types.Cast<object>());
        command.CommandText =
            $"""
            SELECT id
            FROM outbox_messages
            WHERE type IN ({types})
              AND ((status = @pending AND next_attempt_at <= @now) OR (status = @processing AND locked_until < @now))
            ORDER BY next_attempt_at
            LIMIT @batch_size
            FOR UPDATE SKIP LOCKED
            """;
        MySqlParameters.Add(command, "pending", (short)OutboxStatus.Pending);
        MySqlParameters.Add(command, "processing", (short)OutboxStatus.Processing);
        MySqlParameters.Add(command, "now", claim.Now);
        MySqlParameters.Add(command, "batch_size", claim.BatchSize);

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
