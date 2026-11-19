using System.Data.Common;
using System.Globalization;
using ImpiFact.Infrastructure.Platform.Scheduling;

namespace ImpiFact.Infrastructure.MySql;

/// <summary>
/// Bloqueo de una tarea programada en MySQL con un bloqueo con nombre (<c>GET_LOCK</c>): no toca ninguna tabla, lo tiene
/// una sola sesión a la vez y se suelta con <c>RELEASE_LOCK</c>, al cerrarse la sesión o cuando MySqlConnector reinicia
/// la conexión al devolverla al grupo. El nombre es la llave numérica en texto (MySQL limita el nombre a 64 caracteres).
/// </summary>
internal sealed class MySqlScheduledJobLockSql : IScheduledJobLockSql
{
    // GET_LOCK devuelve 1 si lo tomó, 0 si otra sesión lo tiene (con espera 0 no espera) y NULL ante un error.
    public async Task<bool> TryLockAsync(DbConnection connection, long key, CancellationToken ct) =>
        await ExecuteAsync(connection, "SELECT GET_LOCK(@name, 0)", key, ct) is 1L;

    public async Task UnlockAsync(DbConnection connection, long key, CancellationToken ct) =>
        await ExecuteAsync(connection, "SELECT RELEASE_LOCK(@name)", key, ct);

    /// <summary>Nombre del bloqueo de la llave <paramref name="key"/>.</summary>
    internal static string NameOf(long key) => "impifact:job:" + key.ToString(CultureInfo.InvariantCulture);

    private static async Task<long?> ExecuteAsync(DbConnection connection, string sql, long key, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        MySqlParameters.Add(command, "name", NameOf(key));
        var result = await command.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }
}
