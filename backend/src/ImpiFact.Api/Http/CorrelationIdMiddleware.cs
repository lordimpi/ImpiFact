namespace ImpiFact.Api.Http;

/// <summary>
/// Identificador de correlación (Arquitectura detallada, sección 9.1): acepta el que manda el cliente en
/// <c>X-Correlation-Id</c> si es seguro (hasta 64 letras, dígitos, «-», «_», «.» o «:») o genera uno. Queda como
/// identificador de la solicitud (<see cref="HttpContext.TraceIdentifier"/>), en un ámbito de los registros, en el
/// encabezado de la respuesta y en el cuerpo de los errores, para cruzar una queja del cliente con los registros.
/// </summary>
internal sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = IsSafe(context.Request.Headers[HeaderName].ToString())
            ? context.Request.Headers[HeaderName].ToString()
            : Guid.CreateVersion7().ToString("N");
        context.TraceIdentifier = correlationId;

        // El encabezado se pone justo antes de responder: el manejador de excepciones limpia los encabezados que se
        // hayan puesto antes.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    private static bool IsSafe(string value) =>
        value.Length is > 0 and <= MaxLength
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');
}
