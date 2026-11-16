using ImpiFact.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ImpiFact.Infrastructure.Persistence;

/// <summary>
/// Unidad de trabajo sobre el contexto de EF Core (Entity Framework, el mapeador de objetos a tablas) de la
/// solicitud. La usa el decorador de transacción de Application para guardar y confirmar al final del comando.
/// </summary>
internal sealed class EfUnitOfWork(DbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct) =>
        new EfUnitOfWorkTransaction(db, await db.Database.BeginTransactionAsync(ct));
}

/// <summary>
/// Transacción de base de datos. Si se libera sin <see cref="CommitAsync"/>, EF la revierte: nada de lo
/// guardado dentro de ella queda en la base. Si se revierte, o se libera sin confirmar, también descarta los
/// cambios que el contexto tenía en memoria: si no, un guardado posterior en la misma solicitud escribiría lo
/// que se quiso deshacer.
/// </summary>
internal sealed class EfUnitOfWorkTransaction(DbContext db, IDbContextTransaction transaction) : IUnitOfWorkTransaction
{
    private bool committed;

    public async Task CommitAsync(CancellationToken ct)
    {
        await transaction.CommitAsync(ct);
        committed = true;
    }

    public async Task RollbackAsync(CancellationToken ct)
    {
        await transaction.RollbackAsync(ct);
        db.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        if (!committed)
        {
            db.ChangeTracker.Clear();
        }
    }
}
