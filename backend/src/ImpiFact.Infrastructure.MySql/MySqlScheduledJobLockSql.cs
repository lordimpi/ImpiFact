using System.Data.Common;
using System.Globalization;
using ImpiFact.Infrastructure.Platform.Scheduling;

namespace ImpiFact.Infrastructure.MySql;

/// <summary>
/// Bloqueo de una tarea programada en MySQL con un bloqueo con nombre (<c>GET_LOCK</c>): no toca ninguna tabla, lo tiene
/// una sola sesión a la vez y se suelta con <c>RELEASE_LOCK</c>, al cerrarse la sesión o cuando MySqlConnector reinicia
/// la conexión al devolverla al grupo.
/// </summary>
/// <remarks>
/// Los nombres de <c>GET_LOCK</c> valen para todo el servidor, mientras que un advisory lock de PostgreSQL o un
/// <c>sp_getapplock</c> de SQL Server son de su base: dos instalaciones con bases distintas en el mismo servidor MySQL se
/// saltarían las tareas una a la otra. Por eso el nombre lleva el SHA-1 de la base de la conexión (40 caracteres en
/// hexadecimal), dos puntos y la llave numérica: 61 caracteres como mucho, dentro de los 64 que acepta MySQL.
/// </remarks>
internal sealed class MySqlScheduledJobLockSql : IScheduledJobLockSql
{
    /// <summary>Nombre del bloqueo: el mismo al tomarlo y al soltarlo, y distinto en cada base del servidor.</summary>
    private const string NameSql = "CONCAT(SHA1(DATABASE()), ':', @key)";

    // GET_LOCK devuelve 1 si lo tomó, 0 si otra sesión lo tiene (con espera 0 no espera) y NULL ante un error.
    public async Task<bool> TryLockAsync(DbConnection connection, long key, CancellationToken ct) =>
        await ExecuteAsync(connection, $"SELECT GET_LOCK({NameSql}, 0)", key, ct) is 1L;

    public async Task UnlockAsync(DbConnection connection, long key, CancellationToken ct) =>
        await ExecuteAsync(connection, $"SELECT RELEASE_LOCK({NameSql})", key, ct);

    private static async Task<long?> ExecuteAsync(DbConnection connection, string sql, long key, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        MySqlParameters.Add(command, "key", key.ToString(CultureInfo.InvariantCulture));
        var result = await command.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }
}
