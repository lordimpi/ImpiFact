namespace ImpiFact.Application.Features.Companies;

/// <summary>
/// Mensajes de validación con los mismos textos del PHP (<c>resources/lang/es/validation.php</c> de Laravel, el
/// framework del PHP). Laravel nombra el campo con su nombre JSON cambiando «_» por espacios, salvo los pocos que
/// traduce (correo, dirección, teléfono, contraseña); aquí se hace igual para que el campo <c>message</c> se lea
/// como antes.
/// </summary>
internal static class PhpMessages
{
    private static readonly Dictionary<string, string> Attributes = new(StringComparer.Ordinal)
    {
        ["email"] = "correo electrónico",
        ["address"] = "dirección",
        ["phone"] = "teléfono",
        ["password"] = "contraseña",
    };

    public static string Attribute(string field) =>
        Attributes.TryGetValue(field, out var name) ? name : field.Replace('_', ' ');

    public static string Required(string field) => $"El campo {Attribute(field)} es obligatorio.";

    public static string RequiredIf(string field, string other, string value) =>
        $"El campo {Attribute(field)} es obligatorio cuando {Attribute(other)} es {value}.";

    public static string RequiredWith(string field, string other) =>
        $"El campo {Attribute(field)} es obligatorio cuando {Attribute(other)} está presente.";

    public static string Exists(string field) => $"{Attribute(field)} es inválido.";

    public static string Digits(string field, int digits) => $"{Attribute(field)} debe tener {digits} dígitos.";

    public static string DigitsBetween(string field, int min, int max) => $"{Attribute(field)} debe tener entre {min} y {max} dígitos.";

    public static string Email(string field) => $"{Attribute(field)} no es un correo válido";

    public static string MaxLength(string field, int max) => $"{Attribute(field)} no debe ser mayor que {max} caracteres.";

    public static string MinNumber(string field, long min) => $"El campo {Attribute(field)} debe ser como mínimo {min}.";

    public static string MinItems(string field, int min) => $"El tamaño de {Attribute(field)} debe ser de al menos {min}.";

    public static string DateFormat(string field) => $"{Attribute(field)} no corresponde al formato Y-m-d.";

    public static string After(string field, string other) => $"{Attribute(field)} debe ser una fecha posterior a {Attribute(other)}.";

    public static string Url(string field) => $"El formato {Attribute(field)} es inválido.";

    /// <summary>Regla <c>dian_dv</c> del PHP (<c>AppServiceProvider.php</c>).</summary>
    public const string InvalidDv = "Error, el digito de verificacion no es valido para este NIT.";
}
