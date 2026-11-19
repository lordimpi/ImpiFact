using System.Data.Common;
using System.Globalization;
using ImpiFact.Infrastructure.Platform.Scheduling;

namespace ImpiFact.Infrastructure.SqlServer;

/// <summary>
/// Bloqueo de una tarea programada en SQL Server con un bloqueo de aplicación de sesión (<c>sp_getapplock</c> con
/// <c>@LockOwner = 'Session'</c>): un bloqueo con nombre que no toca ninguna tabla, lo tiene una sola sesión a la vez y
/// se suelta con <c>sp_releaseapplock</c> o al cerrarse (o reiniciarse) la sesión. Es el equivalente del advisory lock
/// de PostgreSQL; como él, una misma sesión puede tomarlo varias veces y debe soltarlo otras tantas.
/// </summary>
internal sealed class SqlServerScheduledJobLockSql : IScheduledJobLockSql
{
    // @LockTimeout = 0: no espera. sp_getapplock devuelve 0 o 1 si lo tomó, -1 si otra sesión lo tiene y otro valor
    // negativo ante un error (cancelado, interbloqueo o parámetros inválidos).
    internal const string TryLockSql =
        """
        DECLARE @result int;
        EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
        SELECT @result;
        """;

    // Soltar un bloqueo que la sesión no tiene es un error en SQL Server (en PostgreSQL solo una advertencia): primero
    // se revisa que lo tenga.
    internal const string UnlockSql =
        """
        IF APPLOCK_MODE('public', @resource, 'Session') <> 'NoLock'
            EXEC sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';
        """;

    public async Task<bool> TryLockAsync(DbConnection connection, long key, CancellationToken ct)
    {
        await using var command = Command(connection, TryLockSql, key);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        return result switch
        {
            >= 0 => true,
            -1 => false,
            _ => throw new InvalidOperationException($"sp_getapplock failed with result {result}."),
        };
    }

    public async Task UnlockAsync(DbConnection connection, long key, CancellationToken ct)
    {
        await using var command = Command(connection, UnlockSql, key);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Nombre del bloqueo: la llave numérica de la tarea con un prefijo propio, así no choca con los bloqueos de
    /// aplicación de otros programas en la misma base.
    /// </summary>
    internal static string ResourceOf(long key) => "impifact:scheduled-job:" + key.ToString(CultureInfo.InvariantCulture);

    private static DbCommand Command(DbConnection connection, string sql, long key)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        SqlCommands.Add(command, "resource", ResourceOf(key));
        return command;
    }
}
