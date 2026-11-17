namespace ImpiFact.Application.Features.Billing;

/// <summary>
/// Mensajes de validación con los textos del PHP (<c>resources/lang/es/validation.php</c> de Laravel, el framework del
/// PHP): el campo se nombra con su nombre JSON cambiando «_» por espacios.
/// </summary>
internal static class PlanMessages
{
    public static string Attribute(string field) => field.Replace('_', ' ');

    public static string Required(string field) => $"El campo {Attribute(field)} es obligatorio.";

    public static string DigitsBetween(string field, int min, int max) => $"{Attribute(field)} debe tener entre {min} y {max} dígitos.";

    public static string MinNumber(string field, long min) => $"El campo {Attribute(field)} debe ser como mínimo {min}.";

    public static string In(string field) => $"{Attribute(field)} es inválido.";

    public static string MaxLength(string field, int max) => $"{Attribute(field)} no debe ser mayor que {max} caracteres.";

    public static string Unique(string field) => $"{Attribute(field)} ya ha sido registrado.";
}
