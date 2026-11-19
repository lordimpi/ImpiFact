using System.Globalization;
using System.Text.RegularExpressions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Masters;

/// <summary>
/// Mensajes de validación de clientes y productos, con las mismas frases de la emisión (las de Laravel en español del
/// PHP): así un dato que la emisión rechaza se explica igual en el formulario del maestro.
/// </summary>
internal static partial class MasterMessages
{
    /// <summary>Regla <c>dian_dv</c> del PHP, el mismo texto de la emisión.</summary>
    public const string InvalidDv = "Error, el digito de verificacion no es valido para este NIT.";

    public static string Attribute(string field) => field.Replace('_', ' ');

    public static string Required(string field) => $"El campo {Attribute(field)} es obligatorio.";

    public static string RequiredIf(string field, string other, string value) =>
        $"El campo {Attribute(field)} es obligatorio cuando {Attribute(other)} es {value}.";

    public static string RequiredUnless(string field, string other, string values) =>
        $"El campo {Attribute(field)} es obligatorio a menos que {Attribute(other)} esté en {values}.";

    public static string Prohibited(string field, string other) => $"El campo {Attribute(field)} solo se permite con {Attribute(other)}.";

    public static string Invalid(string field) => $"{Attribute(field)} es inválido.";

    public static string AlphaNum(string field) => $"{Attribute(field)} sólo debe contener letras y números.";

    public static string BetweenLength(string field, int min, int max) => $"{Attribute(field)} tiene que tener entre {min} - {max} caracteres.";

    public static string Digits(string field, int digits) => $"{Attribute(field)} debe tener {digits} dígitos.";

    public static string Email(string field) => $"{Attribute(field)} no es un correo válido";

    public static string MaxLength(string field, int max) => $"{Attribute(field)} no debe ser mayor que {max} caracteres.";

    public static string MinNumber(string field, int min) => $"El campo {field} debe ser como mínimo {min}.";

    public static string MaxNumber(string field, int max) => $"El campo {field} debe ser como máximo {max}.";

    public static string Decimals(string field, int scale) => $"{Attribute(field)} admite como máximo {scale} decimales.";

    public static string ControlCharacters(string field) => $"{Attribute(field)} no debe contener caracteres de control.";

    /// <summary>Correo como lo acepta la regla <c>email</c> de Laravel, igual que la emisión.</summary>
    public static bool IsEmail(string value) => EmailPattern().IsMatch(value);

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}

/// <summary>
/// Dígito de verificación del NIT (Número de Identificación Tributaria) con el algoritmo de la DIAN (la autoridad
/// tributaria de Colombia), igual que <c>validarDigVerifDIAN</c> del PHP y que la emisión. Es una copia porque la regla
/// de módulos no deja usar el validador de otro módulo.
/// </summary>
internal static class MasterCheckDigit
{
    private static readonly int[] Weights = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];

    /// <summary>El NIT debe tener solo dígitos y a lo sumo 15.</summary>
    public static int Of(string nit)
    {
        var sum = 0;
        for (var position = 0; position < nit.Length && position < Weights.Length; position++)
        {
            sum += (nit[nit.Length - 1 - position] - '0') * Weights[position];
        }

        var remainder = sum % 11;
        return remainder is 0 or 1 ? remainder : 11 - remainder;
    }

    public static bool Matches(string identification, string dv) =>
        identification.All(char.IsAsciiDigit) && Of(identification) == int.Parse(dv, CultureInfo.InvariantCulture);
}

/// <summary>Revisa de una sola vez que los ids de catálogo de un cliente o un producto existan.</summary>
internal static class MasterCatalogs
{
    /// <returns>Un error por cada id que no existe en su catálogo (la regla <c>exists</c> del PHP); vacío si todos existen.</returns>
    public static async Task<IReadOnlyList<FieldError>> MissingAsync(
        IDocumentCatalogs catalogs, IEnumerable<(string Field, DianCatalog Catalog, int? Id)> references, CancellationToken ct)
    {
        var wanted = references.Where(reference => reference.Id is not null).ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var found = await catalogs.FindAsync(wanted.Select(reference => new CatalogKey(reference.Catalog, reference.Id!.Value)).Distinct().ToList(), ct);
        return wanted.Where(reference => !found.ContainsKey(new CatalogKey(reference.Catalog, reference.Id!.Value)))
            .Select(reference => new FieldError(reference.Field, MasterMessages.Invalid(reference.Field)))
            .ToList();
    }
}

/// <summary>Texto recortado; nulo si llega vacío o solo con espacios.</summary>
internal static class MasterText
{
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Verdadero si el texto no trae caracteres de control (U+0000 a U+001F). PostgreSQL rechaza U+0000 en una columna o
    /// un parámetro de texto, así que sin esta regla llegaría como un error 500. En los datos de un maestro se aceptan
    /// el tabulador y los saltos de línea (<paramref name="allowLineBreaks"/>); en la búsqueda, no.
    /// </summary>
    public static bool IsPlain(string? value, bool allowLineBreaks) =>
        value is null || !value.Any(character => character < ' ' && !(allowLineBreaks && (character is '\t' or '\n' or '\r')));
}

/// <summary>Errores de los maestros que comparten clientes y productos.</summary>
internal static class MasterErrors
{
    public static readonly Error CustomerNotFound = new("customer.not_found", "El cliente no existe.", ErrorType.NotFound);

    public static readonly Error CustomerDuplicate = new(
        "customer.duplicate", "Ya existe un cliente con ese tipo y número de identificación.", ErrorType.Conflict);

    public static readonly Error ProductNotFound = new("product.not_found", "El producto no existe.", ErrorType.NotFound);

    public static readonly Error ProductDuplicate = new("product.duplicate", "Ya existe un producto con ese código.", ErrorType.Conflict);

    /// <summary>Mismo código y texto que la consulta de rangos de numeración.</summary>
    public static readonly Error CertificateNotFound = new("certificate.not_found", "La empresa no tiene un certificado activo.", ErrorType.NotFound);

    /// <summary>Mismo código y texto que la consulta de rangos de numeración (el del PHP).</summary>
    public static readonly Error CertificateExpired = new("certificate.expired", "El certificado digital ya se encuentra vencido...", ErrorType.Validation);

    public static readonly Error DianUnavailable = new(
        "dian.unavailable", "El servicio de la DIAN no está disponible en este momento. Por favor, inténtelo más tarde.", ErrorType.Unavailable);

    public static Error DianFault(string reason) => new("dian.fault", $"La DIAN rechazó la consulta: {reason}", ErrorType.DianRejected);

    public static Error DianRejected(string? message) => new(
        "dian.rejected", $"La DIAN no pudo completar la consulta: {message ?? "sin detalle"}", ErrorType.DianRejected);
}
