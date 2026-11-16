using Microsoft.AspNetCore.Mvc;

namespace ImpiFact.Api.Http;

/// <summary>
/// Campos que se agregan a todo ProblemDetails (RFC 9457: formato estándar de errores en JSON) que sale de la API,
/// venga de un caso de uso, de una excepción, de la autenticación o del límite de solicitudes (Arquitectura
/// detallada, secciones 9.1 y 9.4): los heredados del PHP <c>success: false</c> y <c>message</c>, y
/// <c>correlation_id</c> para cruzar el error con los registros.
/// </summary>
internal static class LegacyProblemFields
{
    public const string Success = "success";
    public const string Message = "message";
    public const string CorrelationId = "correlation_id";

    public static void Apply(ProblemDetails problem, HttpContext context)
    {
        var status = problem.Status ?? context.Response.StatusCode;
        if (status == StatusCodes.Status500InternalServerError)
        {
            // Un 500 nunca lleva detalles internos (mensaje ni tipo de la excepción).
            problem.Detail = null;
            problem.Extensions.Remove(Message);
        }

        problem.Title = DefaultTitle(status) ?? problem.Title;
        problem.Extensions[Success] = false;
        problem.Extensions.TryAdd(Message, problem.Detail ?? DefaultMessage(status));
        problem.Extensions[CorrelationId] = context.TraceIdentifier;
    }

    /// <summary>Título en español de cada estado HTTP que devuelve la API.</summary>
    public static string? DefaultTitle(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Solicitud inválida",
        StatusCodes.Status401Unauthorized => "No autenticado",
        StatusCodes.Status403Forbidden => "Sin permiso",
        StatusCodes.Status404NotFound => "No encontrado",
        StatusCodes.Status405MethodNotAllowed => "Método no permitido",
        StatusCodes.Status409Conflict => "Conflicto",
        StatusCodes.Status422UnprocessableEntity => "Datos no procesables",
        StatusCodes.Status429TooManyRequests => "Demasiadas solicitudes",
        StatusCodes.Status500InternalServerError => "Error inesperado",
        StatusCodes.Status503ServiceUnavailable => "Servicio no disponible",
        _ => null,
    };

    private static string DefaultMessage(int status) => status switch
    {
        StatusCodes.Status401Unauthorized => "Falta la credencial o no es válida.",
        StatusCodes.Status403Forbidden => "La credencial no tiene permiso para esta operación.",
        StatusCodes.Status404NotFound => "El recurso no existe.",
        StatusCodes.Status429TooManyRequests => "Se superó el límite de solicitudes. Intente de nuevo más tarde.",
        >= StatusCodes.Status500InternalServerError => "Ocurrió un error inesperado. Informe el correlation_id a soporte.",
        _ => DefaultTitle(status) ?? "La solicitud no se pudo atender.",
    };
}
