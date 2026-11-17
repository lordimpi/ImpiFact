namespace ImpiFact.Application.Features.Documents.Queries;

/// <summary>
/// Mensajes de validación con los textos del PHP (<c>resources/lang/es/validation.php</c> de Laravel, el framework
/// del PHP). El PHP no validaba estas rutas; se usan sus mismas frases para que el campo <c>message</c> se lea igual
/// que en el resto de la API.
/// </summary>
internal static class QueryMessages
{
    public static string Date(string field) => $"{field} no es una fecha válida.";

    public static string AfterOrEqual(string field, string other) => $"{field} debe ser una fecha posterior o igual a {other}.";

    public static string Mismatch(string field, string other) => $"{field} no coincide con {other}.";

    public static string Integer(string field) => $"{field} debe ser un número entero.";

    public static string Invalid(string field) => $"{field} es inválido.";

    public static string Required(string field) => $"El campo {field} es obligatorio.";

    public static string MaxLength(string field, int max) => $"{field} no debe ser mayor que {max} caracteres.";

    public static string MinNumber(string field, int min) => $"El campo {field} debe ser como mínimo {min}.";

    public static string MaxNumber(string field, int max) => $"El campo {field} debe ser como máximo {max}.";
}
