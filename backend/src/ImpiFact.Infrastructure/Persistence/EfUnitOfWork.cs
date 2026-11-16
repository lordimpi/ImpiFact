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
        new EfUnitOfWorkTransaction(await db.Database.BeginTransactionAsync(ct));
}

/// <summary>
/// Transacción de base de datos. Si se libera sin <see cref="CommitAsync"/>, EF la revierte: nada de lo
/// guardado dentro de ella queda en la base.
/// </summary>
internal sealed class EfUnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
{
    public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);

    public Task RollbackAsync(CancellationToken ct) => transaction.RollbackAsync(ct);

    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}
