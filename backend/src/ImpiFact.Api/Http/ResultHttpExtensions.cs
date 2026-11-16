using ImpiFact.Application.Abstractions.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ImpiFact.Api.Http;

/// <summary>
/// Traduce el resultado de un caso de uso a una respuesta HTTP (Arquitectura detallada, sección 9.4). Es el único
/// lugar que decide el código HTTP de un <see cref="Error"/>; los endpoints solo llaman a <see cref="ToHttpResult{T}(Result{T})"/>.
/// </summary>
/// <remarks>
/// El cuerpo de error es un ProblemDetails (RFC 9457) con <c>code</c> (código estable del error), <c>errors</c>
/// por campo cuando el error los trae (validación y rechazos de la DIAN) y los campos heredados del PHP
/// <c>success</c> y <c>message</c> (<see cref="LegacyProblemFields"/>).
/// </remarks>
public static class ResultHttpExtensions
{
    /// <summary>Segundos que se sugiere esperar ante un 503 (encabezado <c>Retry-After</c>).</summary>
    public const int RetryAfterSeconds = 30;

    public static IResult ToHttpResult<T>(this Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToHttpResult();

    /// <param name="onSuccess">Respuesta para el caso exitoso, por ejemplo un 201 con su ubicación.</param>
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Error.ToHttpResult();

    public static IResult ToHttpResult(this Error error)
    {
        var status = StatusCodeOf(error.Type);
        var problem = new ProblemDetails { Status = status };
        if (error.Type != ErrorType.Unexpected)
        {
            // Un error inesperado no muestra su mensaje ni su código: pueden traer detalles internos.
            problem.Detail = error.Message;
            problem.Extensions[LegacyProblemFields.Message] = error.Message;
            problem.Extensions["code"] = error.Code;
        }

        if (error.Fields.Count > 0 && error.Type is ErrorType.Validation or ErrorType.DianRejected)
        {
            problem.Extensions["errors"] = error.Fields
                .GroupBy(field => field.Field, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(field => field.Message).ToArray(), StringComparer.Ordinal);
        }

        return new ErrorHttpResult(problem, error.Type == ErrorType.Unavailable ? RetryAfterSeconds : null);
    }

    /// <summary>Código HTTP de cada clase de error.</summary>
    public static int StatusCodeOf(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.DianRejected => StatusCodes.Status422UnprocessableEntity,
        ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    /// <summary>Escribe el ProblemDetails y, si corresponde, el encabezado <c>Retry-After</c>.</summary>
    private sealed class ErrorHttpResult(ProblemDetails problem, int? retryAfterSeconds) : IResult, IStatusCodeHttpResult
    {
        public int? StatusCode => problem.Status;

        public Task ExecuteAsync(HttpContext httpContext)
        {
            LegacyProblemFields.Apply(problem, httpContext);
            if (retryAfterSeconds is { } seconds)
            {
                httpContext.Response.Headers[HeaderNames.RetryAfter] = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return TypedResults.Problem(problem).ExecuteAsync(httpContext);
        }
    }
}
