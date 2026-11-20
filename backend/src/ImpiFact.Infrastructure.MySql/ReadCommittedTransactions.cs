using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ImpiFact.Infrastructure.MySql;

/// <summary>
/// Toda transacción que EF Core (Entity Framework) abre sin nivel de aislamiento, las de <c>BeginTransactionAsync()</c> y
/// las que <c>SaveChanges</c> abre por su cuenta, corre en <c>READ COMMITTED</c> como en PostgreSQL y SQL Server
/// (Modelo de datos detallado, sección 8, punto 8).
/// </summary>
/// <remarks>
/// El <c>READ COMMITTED</c> de la sesión no alcanza: MySqlConnector convierte «sin nivel» en un
/// <c>SET TRANSACTION ISOLATION LEVEL REPEATABLE READ</c> explícito antes de cada <c>START TRANSACTION</c>. Aquí la
/// transacción se abre con el nivel pedido y EF usa esa en lugar de abrir la suya. Una transacción que ya pide un nivel
/// se deja como está.
/// </remarks>
internal sealed class ReadCommittedTransactions : DbTransactionInterceptor
{
    /// <summary>Una sola instancia: no guarda estado y así todos los contextos comparten el mismo interceptor.</summary>
    public static ReadCommittedTransactions Instance { get; } = new();

    public override InterceptionResult<DbTransaction> TransactionStarting(
        DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result) =>
        eventData.IsolationLevel == IsolationLevel.Unspecified && !result.HasResult
            ? InterceptionResult<DbTransaction>.SuppressWithResult(connection.BeginTransaction(IsolationLevel.ReadCommitted))
            : result;

    public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default) =>
        eventData.IsolationLevel == IsolationLevel.Unspecified && !result.HasResult
            ? InterceptionResult<DbTransaction>.SuppressWithResult(
                await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken))
            : result;
}
