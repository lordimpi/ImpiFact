using System.Data.Common;
using ImpiFact.Infrastructure.Documents;

namespace ImpiFact.Infrastructure.SqlServer;

/// <summary>
/// Consecutivo en SQL Server (Modelo de datos detallado, sección 5): <c>UPDATE … OUTPUT</c> cambia la fila y devuelve
/// el valor en una sola sentencia. El UPDATE toma un bloqueo exclusivo de la fila hasta el final de la transacción
/// (también con <c>READ_COMMITTED_SNAPSHOT</c>, que solo cambia las lecturas): una segunda solicitud con el mismo
/// consecutivo espera y luego lee el valor ya avanzado, así nunca obtiene el mismo número.
/// </summary>
internal sealed class SqlServerNumberingSql : INumberingSql
{
    // deleted.next_number es el valor de antes del UPDATE: el número tomado.
    internal const string TakeNextSql =
        """
        UPDATE numbering_sequences
        SET next_number = next_number + 1, row_version = row_version + 1, updated_at = @now
        OUTPUT deleted.next_number
        WHERE company_id = @company_id AND scope_key = @scope_key
        """;

    // GREATEST llega recién en SQL Server 2022: CASE da lo mismo también en 2019.
    internal const string RaiseAboveSql =
        """
        UPDATE numbering_sequences
        SET next_number = CASE WHEN next_number > @number + 1 THEN next_number ELSE @number + 1 END,
            row_version = row_version + 1, updated_at = @now
        OUTPUT inserted.next_number
        WHERE company_id = @company_id AND scope_key = @scope_key
        """;

    public Task<long?> TakeNextAsync(
        DbConnection connection, DbTransaction transaction, Guid companyId, string scopeKey, DateTimeOffset now, CancellationToken ct) =>
        ExecuteAsync(connection, transaction, TakeNextSql, companyId, scopeKey, now, number: null, ct);

    public Task<long?> RaiseAboveAsync(
        DbConnection connection, DbTransaction transaction, Guid companyId, string scopeKey, long number, DateTimeOffset now,
        CancellationToken ct) =>
        ExecuteAsync(connection, transaction, RaiseAboveSql, companyId, scopeKey, now, number, ct);

    private static async Task<long?> ExecuteAsync(
        DbConnection connection, DbTransaction transaction, string sql, Guid companyId, string scopeKey, DateTimeOffset now, long? number,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        SqlCommands.Add(command, "company_id", companyId);
        SqlCommands.Add(command, "scope_key", scopeKey);
        // Las columnas guardan el instante en UTC, como las escribe EF Core (UtcDateTimeOffsetConverter).
        SqlCommands.Add(command, "now", now.ToUniversalTime());
        if (number is { } value)
        {
            SqlCommands.Add(command, "number", value);
        }

        var result = await command.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
