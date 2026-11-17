using ImpiFact.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ImpiFact.Infrastructure.Persistence;

/// <summary>
/// Unidad de trabajo sobre el contexto de EF Core (Entity Framework, el mapeador de objetos a tablas) de la
/// solicitud. La usa el decorador de transacción de Application para guardar y confirmar al final del comando.
/// </summary>
/// <remarks>
/// Si algún adaptador dejó escrituras atómicas pendientes (<see cref="IPendingWrites"/>), el guardado las ejecuta en
/// su misma transacción, después de los cambios de EF: en la transacción que abrió quien llama o, si no hay, en una
/// propia que se confirma al terminar. Si los cambios de EF fallan, las escrituras no se ejecutan.
/// </remarks>
internal sealed class EfUnitOfWork(DbContext db, IDatabaseProvider provider, IEnumerable<IPendingWrites> pendingWrites) : IUnitOfWork
{
    private readonly IReadOnlyList<IPendingWrites> pendingWrites = [.. pendingWrites];

    public async Task<int> SaveChangesAsync(CancellationToken ct)
    {
        if (!pendingWrites.Any(writes => writes.HasPending))
        {
            return await SaveEntitiesAsync(ct);
        }

        if (db.Database.CurrentTransaction is { } current)
        {
            var count = await SaveEntitiesAsync(ct);
            await FlushAsync(current, ct);
            return count;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var saved = await SaveEntitiesAsync(ct);
        await FlushAsync(transaction, ct);
        await transaction.CommitAsync(ct);
        return saved;
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct) =>
        new EfUnitOfWorkTransaction(db, await db.Database.BeginTransactionAsync(ct), pendingWrites);

    // Los choques entre solicitudes simultáneas salen como PersistenceConflictException, que Application entiende sin
    // conocer EF ni el motor de base de datos.
    private async Task<int> SaveEntitiesAsync(CancellationToken ct)
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

    private async Task FlushAsync(IDbContextTransaction transaction, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        foreach (var writes in pendingWrites.Where(writes => writes.HasPending))
        {
            await writes.FlushAsync(connection, transaction.GetDbTransaction(), ct);
        }
    }
}

/// <summary>
/// Transacción de base de datos. Si se libera sin <see cref="CommitAsync"/>, EF la revierte: nada de lo
/// guardado dentro de ella queda en la base. Si se revierte, o se libera sin confirmar, también descarta los
/// cambios que el contexto tenía en memoria y las escrituras atómicas pendientes: si no, un guardado posterior en la
/// misma solicitud escribiría lo que se quiso deshacer.
/// </summary>
internal sealed class EfUnitOfWorkTransaction(DbContext db, IDbContextTransaction transaction, IReadOnlyList<IPendingWrites> pendingWrites)
    : IUnitOfWorkTransaction
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
        Discard();
    }

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        if (!committed)
        {
            Discard();
        }
    }

    private void Discard()
    {
        db.ChangeTracker.Clear();
        foreach (var writes in pendingWrites)
        {
            writes.Discard();
        }
    }
}
