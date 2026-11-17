using System.Data.Common;
using ImpiFact.Infrastructure.Platform.Scheduling;

namespace ImpiFact.Infrastructure.Postgres;

/// <summary>
/// Bloqueo de una tarea programada en PostgreSQL con un advisory lock de sesión (<c>pg_try_advisory_lock</c>): un
/// bloqueo con nombre numérico que no toca ninguna tabla, lo tiene una sola sesión a la vez y se suelta con
/// <c>pg_advisory_unlock</c> o al cerrarse la sesión.
/// </summary>
internal sealed class PostgresScheduledJobLockSql : IScheduledJobLockSql
{
    public async Task<bool> TryLockAsync(DbConnection connection, long key, CancellationToken ct) =>
        await ExecuteAsync(connection, "SELECT pg_try_advisory_lock(@key)", key, ct);

    public async Task UnlockAsync(DbConnection connection, long key, CancellationToken ct) =>
        await ExecuteAsync(connection, "SELECT pg_advisory_unlock(@key)", key, ct);

    private static async Task<bool> ExecuteAsync(DbConnection connection, string sql, long key, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = key;
        command.Parameters.Add(parameter);
        return await command.ExecuteScalarAsync(ct) is true;
    }
}
