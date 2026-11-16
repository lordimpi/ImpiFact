using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using Microsoft.AspNetCore.Diagnostics;

namespace ImpiFact.Api.Http;

/// <summary>
/// Si la base de datos de credenciales no responde (por ejemplo, al buscar una API key durante la autenticación),
/// la API contesta 503 (servicio no disponible) con <c>Retry-After</c>, no 500: el cliente puede reintentar. La
/// respuesta es el mismo ProblemDetails (RFC 9457, formato estándar de errores en JSON) de cualquier error y no
/// lleva detalles internos de la falla.
/// </summary>
internal sealed class CredentialStoreUnavailableHandler : IExceptionHandler
{
    public static readonly Error Unavailable = new(
        "identity.store_unavailable",
        "El servicio de autenticación no está disponible. Intente de nuevo en unos segundos.",
        ErrorType.Unavailable);

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not CredentialStoreUnavailableException)
        {
            return false;
        }

        await Unavailable.ToHttpResult().ExecuteAsync(httpContext);
        return true;
    }
}
