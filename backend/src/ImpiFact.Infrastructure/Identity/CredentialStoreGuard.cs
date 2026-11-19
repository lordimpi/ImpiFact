using System.Data.Common;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Infrastructure.Persistence;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Traduce una falla de conexión con la base de datos (un error transitorio: servidor caído, red, tiempo de
/// espera) en <see cref="CredentialStoreUnavailableException"/>, que la API responde con un 503. Los demás errores
/// (por ejemplo, una restricción violada) siguen como están: son inesperados y terminan en un 500. Qué error es
/// transitorio lo dice el motor de base de datos elegido (<see cref="IDatabaseProvider.IsTransient"/>).
/// </summary>
internal sealed class CredentialStoreGuard(IDatabaseProvider provider)
{
    public async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            throw new CredentialStoreUnavailableException(exception);
        }
    }

    public Task RunAsync(Func<Task> operation) => RunAsync(async () =>
    {
        await operation();
        return true;
    });

    private bool IsUnavailable(Exception exception) =>
        exception is TimeoutException
        || (exception is DbException database && provider.IsTransient(database))
        || (exception.InnerException is { } inner && IsUnavailable(inner));
}
