using ImpiFact.Application.Abstractions.Identity;

namespace ImpiFact.Application.Features.Identity.Sessions;

/// <summary>
/// Una revocación de sesiones con su propio plazo, sin la cancelación de la petición: el cierre de sesión
/// (<c>POST /api/v1/auth/logout</c>) y la respuesta a un token de renovación reusado deben terminar aunque el navegador
/// corte la petición. Si ese plazo se cumple, el motor de base de datos cancela la consulta y lanza
/// <see cref="OperationCanceledException"/>; aquí se convierte en <see cref="CredentialStoreUnavailableException"/>, el
/// mismo 503 (<c>identity.store_unavailable</c>) que la API da cuando la base no responde, en lugar de un 500.
/// </summary>
/// <remarks>
/// Solo se convierte la cancelación cuando la causa es este plazo. Una cancelación que venga de otro lado (por ejemplo,
/// de la petición que el cliente abandonó) sigue siendo una cancelación.
/// </remarks>
public static class SessionRevocation
{
    public static async Task<T> WithinDeadlineAsync<T>(TimeSpan timeout, Func<CancellationToken, Task<T>> revoke)
    {
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            return await revoke(deadline.Token);
        }
        catch (Exception exception) when (deadline.IsCancellationRequested && IsCancellation(exception))
        {
            throw new CredentialStoreUnavailableException(exception);
        }
    }

    public static Task WithinDeadlineAsync(TimeSpan timeout, Func<CancellationToken, Task> revoke) =>
        WithinDeadlineAsync(timeout, async token =>
        {
            await revoke(token);
            return true;
        });

    private static bool IsCancellation(Exception exception) =>
        exception is OperationCanceledException || (exception.InnerException is { } inner && IsCancellation(inner));
}
