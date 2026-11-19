using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Platform.Outbox;

namespace ImpiFact.Infrastructure.SqlServer;

/// <summary>
/// Toma de la bandeja de salida (outbox: la tabla <c>outbox_messages</c> con el trabajo pendiente del Worker) en SQL
/// Server. La expresión de tabla <c>due</c> elige las filas con <c>UPDLOCK, READPAST, ROWLOCK</c>: bloquea cada fila
/// elegida para modificarla y salta las que otra transacción ya bloqueó, así dos instancias que consultan a la vez
/// reciben mensajes distintos sin esperarse (el equivalente de <c>FOR UPDATE SKIP LOCKED</c>). El <c>UPDATE</c> sobre
/// esa expresión las marca en proceso y <c>OUTPUT</c> las devuelve en la misma sentencia.
/// </summary>
internal sealed class SqlServerOutboxQueueSql : IOutboxQueueSql
{
    /// <summary>
    /// La sentencia con un parámetro por tipo (<c>@type0, @type1…</c>): SQL Server no recibe listas como parámetro.
    /// </summary>
    internal static string ClaimSql(int typeCount) =>
        $"""
        WITH due AS (
            SELECT TOP (@batch_size) id, company_id, type, payload, attempts, next_attempt_at, status, locked_by, locked_until
            FROM outbox_messages WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE type IN ({string.Join(", ", Enumerable.Range(0, typeCount).Select(TypeParameter))})
              AND ((status = @pending AND next_attempt_at <= @now) OR (status = @processing AND locked_until < @now))
            ORDER BY next_attempt_at
        )
        UPDATE due
        SET status = @processing, locked_by = @worker_id, locked_until = @locked_until, attempts = attempts + 1
        OUTPUT inserted.id, inserted.company_id, inserted.type, inserted.payload, inserted.attempts, inserted.next_attempt_at
        """;

    public async Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimAsync(DbConnection connection, OutboxClaim claim, CancellationToken ct)
    {
        // Sin tipos no hay nada que tomar (en PostgreSQL «= ANY» de una lista vacía tampoco devuelve filas); «IN ()» ni
        // siquiera es SQL válido.
        var types = claim.Types.ToList();
        if (types.Count == 0)
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        command.CommandText = ClaimSql(types.Count);
        SqlCommands.Add(command, "processing", (short)OutboxStatus.Processing);
        SqlCommands.Add(command, "pending", (short)OutboxStatus.Pending);
        SqlCommands.Add(command, "worker_id", claim.WorkerId);
        for (var index = 0; index < types.Count; index++)
        {
            SqlCommands.Add(command, TypeParameter(index)[1..], types[index]);
        }

        SqlCommands.Add(command, "batch_size", claim.BatchSize);
        // Las columnas guardan el instante en UTC, como las escribe EF Core (UtcDateTimeOffsetConverter).
        SqlCommands.Add(command, "now", claim.Now.ToUniversalTime());
        SqlCommands.Add(command, "locked_until", claim.LockedUntil.ToUniversalTime());

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

        // OUTPUT no garantiza orden: se ordena aquí como en la expresión de tabla.
        return [.. claimed.OrderBy(message => message.NextAttemptAt)];
    }

    private static string TypeParameter(int index) => "@type" + index.ToString(CultureInfo.InvariantCulture);
}
