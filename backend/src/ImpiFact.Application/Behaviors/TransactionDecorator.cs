using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Behaviors;

/// <summary>
/// Último decorador antes del caso de uso, solo para comandos (<see cref="ICommand"/>): abre una
/// transacción y, si el caso de uso sale bien, guarda los cambios y confirma. Si falla, la revierte. Si
/// lanza una excepción, la transacción se libera sin confirmar y también se revierte. Las consultas pasan
/// directo, sin transacción.
/// </summary>
internal sealed class TransactionDecorator<TIn, TOut>(IHandler<TIn, TOut> inner, IUnitOfWork unitOfWork) : IHandler<TIn, TOut>
{
    private static readonly bool IsCommand = typeof(ICommand).IsAssignableFrom(typeof(TIn));

    public async Task<Result<TOut>> HandleAsync(TIn request, CancellationToken ct)
    {
        if (!IsCommand)
        {
            return await inner.HandleAsync(request, ct);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);

        var result = await inner.HandleAsync(request, ct);
        if (result.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        else
        {
            await transaction.RollbackAsync(ct);
        }

        return result;
    }
}
