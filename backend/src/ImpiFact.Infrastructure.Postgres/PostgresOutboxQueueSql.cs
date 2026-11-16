using System.Data.Common;
using System.Text.Json.Nodes;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Platform.Outbox;

namespace ImpiFact.Infrastructure.Postgres;

/// <summary>
/// Toma de la bandeja de salida (outbox: la tabla <c>outbox_messages</c> con el trabajo pendiente del Worker) en
/// PostgreSQL. La subconsulta elige las filas con <c>FOR UPDATE SKIP LOCKED</c>: las bloquea y salta las que otra
/// transacción ya bloqueó, así dos instancias que consultan a la vez reciben mensajes distintos sin esperarse. El
/// <c>UPDATE … RETURNING</c> las marca en proceso y las devuelve en la misma sentencia.
/// </summary>
internal sealed class PostgresOutboxQueueSql : IOutboxQueueSql
{
    internal const string ClaimSql =
        """
        UPDATE outbox_messages AS message
        SET status = @processing, locked_by = @worker_id, locked_until = @locked_until, attempts = message.attempts + 1
        FROM (
            SELECT id
            FROM outbox_messages
            WHERE type = ANY(@types)
              AND ((status = @pending AND next_attempt_at <= @now) OR (status = @processing AND locked_until < @now))
            ORDER BY next_attempt_at
            LIMIT @batch_size
            FOR UPDATE SKIP LOCKED
        ) AS due
        WHERE message.id = due.id
        RETURNING message.id, message.company_id, message.type, message.payload, message.attempts, message.next_attempt_at
        """;

    public async Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimAsync(DbConnection connection, OutboxClaim claim, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = ClaimSql;
        Add(command, "processing", (short)OutboxStatus.Processing);
        Add(command, "pending", (short)OutboxStatus.Pending);
        Add(command, "worker_id", claim.WorkerId);
        Add(command, "types", claim.Types.ToArray());
        Add(command, "batch_size", claim.BatchSize);
        // timestamptz (instante con zona) solo acepta DateTimeOffset en UTC.
        Add(command, "now", claim.Now.ToUniversalTime());
        Add(command, "locked_until", claim.LockedUntil.ToUniversalTime());

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
                reader.GetFieldValue<DateTimeOffset>(5)));
        }

        // RETURNING no garantiza orden: se ordena aquí como en la subconsulta.
        return [.. claimed.OrderBy(message => message.NextAttemptAt)];
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
