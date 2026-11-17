using System.Buffers.Binary;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using ImpiFact.Application.Abstractions;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Infrastructure.Platform.Scheduling;

/// <summary>
/// SQL propio de cada motor para el bloqueo de una tarea programada. En PostgreSQL es un advisory lock (un bloqueo
/// con nombre que la base lleva por sesión y que no toca ninguna tabla). Cada proyecto
/// <c>ImpiFact.Infrastructure.&lt;Motor&gt;</c> trae su implementación y la registra con su <c>IDatabaseProvider</c>.
/// </summary>
public interface IScheduledJobLockSql
{
    /// <summary>Intenta tomar el bloqueo <paramref name="key"/> en la sesión de <paramref name="connection"/> sin esperar.</summary>
    /// <returns>Falso si otra sesión lo tiene.</returns>
    Task<bool> TryLockAsync(DbConnection connection, long key, CancellationToken ct);

    /// <summary>Suelta el bloqueo <paramref name="key"/> que tomó la sesión de <paramref name="connection"/>.</summary>
    Task UnlockAsync(DbConnection connection, long key, CancellationToken ct);
}

/// <summary>
/// Bloqueos de las tareas programadas: dos instancias del Worker nunca corren la misma tarea a la vez. El bloqueo vive
/// en una conexión abierta solo para él mientras dura la tarea; si la instancia se cae, la base cierra la sesión y el
/// bloqueo se suelta solo, sin esperar ningún plazo.
/// </summary>
internal sealed class ScheduledJobLocks(IServiceScopeFactory scopes, IScheduledJobLockSql sql)
{
    /// <summary>Toma el bloqueo de la tarea <paramref name="jobName"/> sin esperar.</summary>
    /// <returns>El bloqueo tomado, que se suelta al desecharlo; nulo si otra instancia lo tiene.</returns>
    public async Task<IAsyncDisposable?> TryAcquireAsync(string jobName, CancellationToken ct)
    {
        var key = KeyOf(jobName);
        var scope = scopes.CreateAsyncScope();
        try
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(companyId: null, userId: null, ActorType.System);
            var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
            await db.Database.OpenConnectionAsync(ct);
            if (await sql.TryLockAsync(db.Database.GetDbConnection(), key, ct))
            {
                return new Held(scope, db, sql, key);
            }
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }

        await scope.DisposeAsync();
        return null;
    }

    /// <summary>
    /// Llave numérica del bloqueo: los primeros 8 bytes del SHA-256 del nombre de la tarea, siempre la misma en todas
    /// las instancias y sin chocar con los bloqueos de otras tareas.
    /// </summary>
    internal static long KeyOf(string jobName) =>
        BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes("impifact:scheduled-job:" + jobName)));

    private sealed class Held(AsyncServiceScope scope, ImpiFactDbContext db, IScheduledJobLockSql sql, long key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                // Sin el token de apagado: el bloqueo se suelta aunque el Worker se esté deteniendo. Si falla, cerrar la
                // conexión lo suelta igual, porque la conexión vuelve al grupo con su sesión reiniciada.
                await sql.UnlockAsync(db.Database.GetDbConnection(), key, CancellationToken.None);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
                await scope.DisposeAsync();
            }
        }
    }
}
