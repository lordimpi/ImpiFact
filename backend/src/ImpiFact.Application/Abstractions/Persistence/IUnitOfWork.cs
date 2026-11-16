namespace ImpiFact.Application.Abstractions.Persistence;

/// <summary>
/// Unidad de trabajo sobre la base de datos (Arquitectura detallada, sección 4.4). Los handlers de
/// comandos no la llaman: el decorador de transacción guarda los cambios y confirma al final.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);

    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct);
}

/// <summary>
/// Una transacción abierta. Si se libera sin llamar a <see cref="CommitAsync"/>, se revierte.
/// </summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);

    Task RollbackAsync(CancellationToken ct);
}
