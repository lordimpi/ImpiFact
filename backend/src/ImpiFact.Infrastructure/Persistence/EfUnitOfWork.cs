using ImpiFact.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ImpiFact.Infrastructure.Persistence;

/// <summary>
/// Unidad de trabajo sobre el contexto de EF Core (Entity Framework, el mapeador de objetos a tablas) de la
/// solicitud. La usa el decorador de transacción de Application para guardar y confirmar al final del comando.
/// </summary>
internal sealed class EfUnitOfWork(DbContext db, IDatabaseProvider provider) : IUnitOfWork
{
    // Los choques entre solicitudes simultáneas salen como PersistenceConflictException, que Application entiende sin
    // conocer EF ni el motor de base de datos.
    public async Task<int> SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            return await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PersistenceConflictException("Another request changed the same rows.", exception);
        }
        catch (DbUpdateException exception) when (provider.IsUniqueViolation(exception))
        {
            throw new PersistenceConflictException("Another request stored the same unique value.", exception);
        }
    }

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
