using System.Data.Common;
using System.Globalization;
using ImpiFact.Infrastructure.Documents;

namespace ImpiFact.Infrastructure.MySql;

/// <summary>
/// Consecutivo en MySQL (Modelo de datos detallado, sección 5). MySQL no tiene <c>RETURNING</c>: el <c>UPDATE</c> guarda
/// el valor que interesa con <c>LAST_INSERT_ID(expr)</c>, que lo deja en la sesión, y el <c>SELECT</c> siguiente lo lee
/// en el mismo comando y la misma conexión. InnoDB bloquea la fila hasta el final de la transacción, así una segunda
/// solicitud con el mismo consecutivo espera y luego lee el valor ya avanzado: nunca obtiene el mismo número.
/// </summary>
/// <remarks>
/// Si el <c>UPDATE</c> no encuentra la fila, <c>LAST_INSERT_ID()</c> conserva el valor de antes; por eso el
/// <c>SELECT</c> mira <c>ROW_COUNT()</c> (filas encontradas, porque MySqlConnector las pide así) y devuelve nulo.
/// </remarks>
internal sealed class MySqlNumberingSql : INumberingSql
{
    // MySQL evalúa las asignaciones de izquierda a derecha: LAST_INSERT_ID(next_number) guarda el valor anterior.
    internal const string TakeNextSql =
        """
        UPDATE numbering_sequences
        SET next_number = LAST_INSERT_ID(next_number) + 1, row_version = row_version + 1, updated_at = @now
        WHERE company_id = @company_id AND scope_key = @scope_key;
        SELECT IF(ROW_COUNT() > 0, LAST_INSERT_ID(), NULL);
        """;

    internal const string RaiseAboveSql =
        """
        UPDATE numbering_sequences
        SET next_number = LAST_INSERT_ID(GREATEST(next_number, @number + 1)), row_version = row_version + 1, updated_at = @now
        WHERE company_id = @company_id AND scope_key = @scope_key;
        SELECT IF(ROW_COUNT() > 0, LAST_INSERT_ID(), NULL);
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
        MySqlParameters.Add(command, "company_id", companyId);
        MySqlParameters.Add(command, "scope_key", scopeKey);
        MySqlParameters.Add(command, "now", now);
        if (number is { } value)
        {
            MySqlParameters.Add(command, "number", value);
        }

        var result = await command.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }
}
