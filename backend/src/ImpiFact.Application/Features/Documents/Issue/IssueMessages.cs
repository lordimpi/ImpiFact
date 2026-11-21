using System.Text.RegularExpressions;

namespace ImpiFact.Application.Features.Documents.Issue;

/// <summary>
/// Mensajes de validación con los textos del PHP (<c>resources/lang/es/validation.php</c> de Laravel, el framework del
/// PHP). Laravel nombra el campo con su ruta JSON (<c>invoice_lines.0.description</c>) cambiando «_» por espacios;
/// aquí se hace igual para que el campo <c>message</c> se lea como antes.
/// </summary>
internal static partial class IssueMessages
{
    public static string Attribute(string field) => field.Replace('_', ' ');

    public static string Required(string field) => $"El campo {Attribute(field)} es obligatorio.";

    public static string RequiredIf(string field, string other, string value) =>
        $"El campo {Attribute(field)} es obligatorio cuando {Attribute(other)} es {value}.";

    public static string RequiredWith(string field, string other) =>
        $"El campo {Attribute(field)} es obligatorio cuando {Attribute(other)} está presente.";

    public static string RequiredUnless(string field, string other, string values) =>
        $"El campo {Attribute(field)} es obligatorio a menos que {Attribute(other)} esté en {values}.";

    public static string Numeric(string field) => $"{Attribute(field)} debe ser numérico.";

    public static string Integer(string field) => $"{Attribute(field)} debe ser un número entero.";

    public static string Boolean(string field) => $"El campo {Attribute(field)} debe tener un valor verdadero o falso.";

    public static string Invalid(string field) => $"{Attribute(field)} es inválido.";

    public static string DateFormat(string field, string format) => $"{Attribute(field)} no corresponde al formato {format}.";

    public static string AfterOrEqual(string field, string date) => $"{Attribute(field)} debe ser una fecha posterior o igual a {date}.";

    public static string BeforeOrEqual(string field, string date) => $"{Attribute(field)} debe ser una fecha anterior o igual a {date}.";

    public static string Between(string field, long min, long max) =>
        $"{Attribute(field)} tiene que estar entre {min} - {max} o no se establecio el atributo que define el minimo y maximo.";

    public static string BetweenLength(string field, int min, int max) => $"{Attribute(field)} tiene que tener entre {min} - {max} caracteres.";

    public static string AlphaNum(string field) => $"{Attribute(field)} sólo debe contener letras y números.";

    public static string Digits(string field, int digits) => $"{Attribute(field)} debe tener {digits} dígitos.";

    public static string DigitsBetween(string field, int min, int max) => $"{Attribute(field)} debe tener entre {min} y {max} dígitos.";

    public static string Email(string field) => $"{Attribute(field)} no es un correo válido";

    public static string Size(string field, int size) => $"{Attribute(field)} debe contener {size} caracteres.";

    public static string MaxLength(string field, int max) => $"{Attribute(field)} no debe ser mayor que {max} caracteres.";

    public static string MaxNumber(string field, decimal max) => $"{Attribute(field)} no debe ser mayor a {max:0}.";

    public static string Array(string field) => $"{Attribute(field)} debe ser un conjunto.";

    public static string Positive(string field) => $"{Attribute(field)} debe ser mayor que 0.";

    /// <summary>The Laravel <c>in</c> message plus the accepted Incoterms, so the client knows what to send.</summary>
    public static string Incoterm(string field) =>
        $"{Attribute(field)} es inválido. Use uno de estos Incoterms: CFR, CIF, CIP, CPT, DAP, DAT, DDP, EXW, FAS, FCA o FOB.";

    /// <summary>Export invoice in another currency without its exchange rate: ImpiFact never fetches nor invents it.</summary>
    public static string ExchangeRateRequired(string field, string currency, string localCurrency) =>
        $"El campo {Attribute(field)} es obligatorio cuando idcurrency es {currency} y no {localCurrency}: indique la tasa de cambio a " +
        $"{localCurrency} (la TRM o la pactada con el comprador). ImpiFact no la consulta en línea ni la supone.";

    public static string ExchangeRateDateRequired(string field, string currency, string localCurrency) =>
        $"El campo {Attribute(field)} es obligatorio cuando idcurrency es {currency} y no {localCurrency}: indique la fecha de la tasa " +
        "de cambio (Y-m-d).";

    /// <summary>Regla <c>dian_dv</c> del PHP (<c>AppServiceProvider.php</c>).</summary>
    public const string InvalidDv = "Error, el digito de verificacion no es valido para este NIT.";

    /// <summary>Mensaje de la regla <c>ResolutionSetting</c> del PHP.</summary>
    public const string ResolutionNotConfigured = "La resolución no está configurada.";

    /// <summary>Correo como lo acepta la regla <c>email</c> de Laravel, sin pretender validar todo el estándar.</summary>
    public static bool IsEmail(string value) => EmailPattern().IsMatch(value);

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
