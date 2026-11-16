namespace ImpiFact.Application.Abstractions.Results;

/// <summary>
/// Error esperado de un caso de uso (Arquitectura detallada, sección 4.1). Los flujos previstos, como un
/// dato inválido o un rechazo de la DIAN, se devuelven como un <see cref="Error"/> dentro de un resultado
/// fallido; las excepciones quedan solo para lo inesperado.
/// </summary>
/// <param name="Code">Código estable para máquinas, por ejemplo <c>company.not_found</c>.</param>
/// <param name="Message">Mensaje en español para la persona que usa la API.</param>
/// <param name="Type">Clase del error; la API la traduce a un código HTTP.</param>
public readonly record struct Error(string Code, string Message, ErrorType Type)
{
    private readonly IReadOnlyList<FieldError>? _fields;

    /// <summary>Errores por campo; solo los errores de validación los traen. Nunca es nulo.</summary>
    public IReadOnlyList<FieldError> Fields
    {
        get => _fields ?? [];
        init => _fields = value;
    }

    public static Error Validation(IReadOnlyList<FieldError> fields) =>
        new("validation.failed", "La solicitud tiene datos inválidos.", ErrorType.Validation) { Fields = fields };
}

/// <summary>Un dato inválido: el campo, con el nombre que trae la solicitud, y el motivo en español.</summary>
public readonly record struct FieldError(string Field, string Message);

/// <summary>Clase de error. Cada una corresponde a un código HTTP (Arquitectura detallada, sección 8).</summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    DianRejected,
    Unavailable,
    Unexpected,
}
