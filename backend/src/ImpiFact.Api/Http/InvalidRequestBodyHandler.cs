using ImpiFact.Application.Abstractions.Results;
using Microsoft.AspNetCore.Diagnostics;

namespace ImpiFact.Api.Http;

/// <summary>
/// Una solicitud que no se puede leer (un cuerpo JSON, JavaScript Object Notation, mal formado o con un campo de
/// tipo equivocado, por ejemplo un número donde va un texto, o un parámetro de la consulta que no se puede
/// convertir) es un error de quien llama, no del servidor: la API responde 422 con el mismo ProblemDetails (RFC 9457, formato estándar de errores en JSON) de los errores de validación. Las rutas
/// mínimas lanzan <see cref="BadHttpRequestException"/> en ese caso porque <c>ThrowOnBadRequest</c> está activo en
/// todos los ambientes (ver <c>ApiComposition</c>); otros estados de esa excepción, como 413 (cuerpo demasiado
/// grande), se responden con su propio estado.
/// </summary>
internal sealed class InvalidRequestBodyHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public static readonly Error InvalidBody = new(
        "request.invalid_body",
        "La solicitud no se pudo leer: el cuerpo no es un JSON válido o algún valor tiene un tipo de dato incorrecto.",
        ErrorType.Validation);

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        if (badRequest.StatusCode == StatusCodes.Status400BadRequest)
        {
            await InvalidBody.ToHttpResult().ExecuteAsync(httpContext);
            return true;
        }

        httpContext.Response.StatusCode = badRequest.StatusCode;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext });
    }
}
