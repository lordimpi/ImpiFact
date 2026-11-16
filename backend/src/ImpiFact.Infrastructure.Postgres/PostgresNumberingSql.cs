using System.Data.Common;
using ImpiFact.Infrastructure.Documents;

namespace ImpiFact.Infrastructure.Postgres;

/// <summary>
/// Consecutivo en PostgreSQL (Modelo de datos detallado, sección 5): <c>UPDATE … RETURNING</c> cambia la fila y
/// devuelve el valor en una sola sentencia. PostgreSQL bloquea la fila hasta el final de la transacción, así una
/// segunda solicitud con el mismo consecutivo espera y luego lee el valor ya avanzado: nunca obtiene el mismo número.
/// </summary>
internal sealed class PostgresNumberingSql : INumberingSql
{
    internal const string TakeNextSql =
        """
        UPDATE numbering_sequences
        SET next_number = next_number + 1, row_version = row_version + 1, updated_at = @now
        WHERE company_id = @company_id AND scope_key = @scope_key
        RETURNING next_number - 1
        """;

    internal const string RaiseAboveSql =
        """
        UPDATE numbering_sequences
        SET next_number = GREATEST(next_number, @number + 1), row_version = row_version + 1, updated_at = @now
        WHERE company_id = @company_id AND scope_key = @scope_key
        RETURNING next_number
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
        Add(command, "company_id", companyId);
        Add(command, "scope_key", scopeKey);
        // timestamptz (instante con zona) solo acepta DateTimeOffset en UTC.
        Add(command, "now", now.ToUniversalTime());
        if (number is { } value)
        {
            Add(command, "number", value);
        }

        var result = await command.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
