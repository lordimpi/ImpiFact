using System.Data.Common;
using ImpiFact.Application.Abstractions.Identity;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Traduce una falla de conexión con la base de datos (un error transitorio: servidor caído, red, tiempo de
/// espera) en <see cref="CredentialStoreUnavailableException"/>, que la API responde con un 503. Los demás errores
/// (por ejemplo, una restricción violada) siguen como están: son inesperados y terminan en un 500.
/// </summary>
internal static class CredentialStoreGuard
{
    public static async Task<T> RunAsync<T>(Func<Task<T>> operation)
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

    public static Task RunAsync(Func<Task> operation) => RunAsync(async () =>
    {
        await operation();
        return true;
    });

    private static bool IsUnavailable(Exception exception) =>
        exception is DbException { IsTransient: true } or TimeoutException
        || (exception.InnerException is { } inner && IsUnavailable(inner));
}
