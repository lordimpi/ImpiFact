using System.Globalization;
using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace ImpiFact.Infrastructure.Observability;

/// <summary>
/// Quita los datos sensibles de cada registro antes de que salga por cualquier destino (consola u OTLP; Arquitectura
/// detallada, sección 12): las propiedades cuyo nombre es de un secreto (<c>*password*</c>, <c>*token*</c>,
/// <c>*secret*</c>, <c>*refresh*</c>, <c>*session*</c>, <c>pin</c>, <c>Authorization</c>, <c>Cookie</c>, API keys y llaves
/// privadas) quedan como <see cref="Mask"/>; en los textos se tapan las credenciales (<c>Bearer …</c>, <c>Basic …</c>,
/// JWT, API keys con el prefijo <c>apk_</c>) y los valores de los parámetros con esos nombres, sea de una consulta
/// (<c>?password=…&amp;token=…</c>) o de una cookie (<c>impifact_refresh=…; Path=/</c>, con o sin comillas); los
/// arreglos de bytes (certificados, archivos) se cambian por su tamaño. Además de los textos, se revisa el texto de los
/// demás valores simples que no tienen un formato fijo, como un <see cref="Uri"/> con su consulta. El texto de las
/// excepciones lo tapa <see cref="ExceptionRedactingSink"/> con <see cref="RedactText"/>.
/// </summary>
/// <remarks>
/// Es una segunda barrera: el código no debe registrar secretos. Corre en el propio Serilog, así cubre también las
/// propiedades que llegan de los ámbitos (scopes) de Microsoft.Extensions.Logging y las del framework, como la
/// <c>QueryString</c> que registra ASP.NET Core al empezar cada solicitud.
/// </remarks>
internal sealed partial class SensitiveDataRedactor : ILogEventEnricher
{
    /// <summary>Lo que queda en lugar de un dato sensible.</summary>
    public const string Mask = "***";

    private static readonly ScalarValue Masked = new(Mask);

    private static readonly string[] SensitiveFragments =
    [
        "password", "passwd", "secret", "token", "authorization", "cookie", "apikey", "privatekey", "signingkey", "masterkey",
        "pfx", "connectionstring",
        // Las cookies de sesión (impifact_refresh, impifact_session y las de otros sistemas) valen lo que una credencial.
        "refresh", "session",
    ];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        List<LogEventProperty>? changed = null;
        foreach (var (name, value) in logEvent.Properties)
        {
            var redacted = IsSensitiveName(name) ? Masked : Redact(value);
            if (!ReferenceEquals(redacted, value))
            {
                (changed ??= []).Add(new LogEventProperty(name, redacted));
            }
        }

        foreach (var property in changed ?? [])
        {
            logEvent.AddOrUpdateProperty(property);
        }
    }

    /// <summary>Si el nombre de una propiedad o de un parámetro es el de un dato sensible.</summary>
    public static bool IsSensitiveName(string name)
    {
        var compact = name.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal)
            .Replace(".", "", StringComparison.Ordinal).ToLowerInvariant();
        return SensitiveFragments.Any(fragment => compact.Contains(fragment, StringComparison.Ordinal))
            // «pin» como palabra entera: «Pin», «UserPin», «pin_code»; no «Shipping» ni «Mapping».
            || Words(name).Any(word => word.Equals("pin", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>El texto sin credenciales ni valores de parámetros sensibles; el mismo objeto si no había nada.</summary>
    public static string RedactText(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var result = Credential().Replace(text, match => match.Groups["scheme"].Value + " " + Mask);
        result = Jwt().Replace(result, Mask);
        result = ApiKey().Replace(result, "apk_" + Mask);
        result = Parameter().Replace(result, match =>
            IsSensitiveName(match.Groups["key"].Value) ? match.Groups["key"].Value + "=" + Mask : match.Value);
        result = LongByteArray().Replace(result, "[${length} bytes]");
        if (HexBytes().IsMatch(result))
        {
            result = $"[{result.Length / 2} bytes]";
        }

        return string.Equals(result, text, StringComparison.Ordinal) ? text : result;
    }

    private static LogEventPropertyValue Redact(LogEventPropertyValue value)
    {
        switch (value)
        {
            case ScalarValue { Value: string text }:
                var redacted = RedactText(text);
                return ReferenceEquals(redacted, text) ? value : new ScalarValue(redacted);
            case ScalarValue { Value: byte[] bytes }:
                return new ScalarValue($"[{bytes.Length} bytes]");
            case ScalarValue { Value: { } scalar } when !HasFixedFormat(scalar):
                // Un Uri (o cualquier otro valor simple sin formato fijo) se escribe con su texto, que puede traer una
                // consulta con un token: se revisa ese texto y, si había algo que tapar, queda como texto.
                var original = Convert.ToString(scalar, CultureInfo.InvariantCulture) ?? "";
                var cleaned = RedactText(original);
                return ReferenceEquals(cleaned, original) ? value : new ScalarValue(cleaned);
            case SequenceValue sequence:
                var elements = sequence.Elements.Select(Redact).ToList();
                return elements.Where((element, index) => !ReferenceEquals(element, sequence.Elements[index])).Any()
                    ? new SequenceValue(elements)
                    : value;
            case StructureValue structure:
                var properties = structure.Properties
                    .Select(property => IsSensitiveName(property.Name) ? new LogEventProperty(property.Name, Masked)
                        : Redact(property.Value) is var inner && !ReferenceEquals(inner, property.Value) ? new LogEventProperty(property.Name, inner)
                        : property)
                    .ToList();
                return properties.Where((property, index) => !ReferenceEquals(property, structure.Properties[index])).Any()
                    ? new StructureValue(properties, structure.TypeTag)
                    : value;
            case DictionaryValue dictionary:
                var changed = false;
                var entries = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>();
                foreach (var (key, entry) in dictionary.Elements)
                {
                    var replaced = key.Value is string name && IsSensitiveName(name) ? Masked : Redact(entry);
                    changed |= !ReferenceEquals(replaced, entry);
                    entries.Add(new(key, replaced));
                }

                return changed ? new DictionaryValue(entries) : value;
            default:
                return value;
        }
    }

    /// <summary>
    /// Los valores simples cuyo texto no puede llevar un secreto: números, booleanos, caracteres, enumeraciones, fechas,
    /// horas, duraciones y Guid. Se escriben tal cual y conservan su tipo (un número sigue siendo número en el JSON).
    /// </summary>
    private static bool HasFixedFormat(object value) =>
        value.GetType().IsPrimitive
        || value is Enum or decimal or Int128 or UInt128 or Half or DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan or Guid;

    private static IEnumerable<string> Words(string name) => WordBoundary().Split(name).Where(word => word.Length > 0);

    [GeneratedRegex(@"(?<scheme>\b(?:Bearer|Basic|ApiKey))\s+[^\s,;""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Credential();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]*", RegexOptions.CultureInvariant)]
    private static partial Regex Jwt();

    [GeneratedRegex(@"\bapk_[A-Za-z0-9_-]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex ApiKey();

    // El valor llega hasta el siguiente separador de una consulta o de una cookie (&, ;, coma o espacio), o entre comillas.
    [GeneratedRegex(@"(?<key>[A-Za-z0-9_.\-]+)=(?<value>""[^""]*""|[^&\s""';,]*)", RegexOptions.CultureInvariant)]
    private static partial Regex Parameter();

    // Serilog escribe un arreglo de más de 1 kB como los primeros bytes en hexadecimal y su tamaño.
    [GeneratedRegex(@"^[0-9A-F]{32}\.\.\. \((?<length>\d+) bytes\)$", RegexOptions.CultureInvariant)]
    private static partial Regex LongByteArray();

    // Y uno más corto, entero en hexadecimal en mayúsculas. Desde 24 bytes (una llave de 32 bytes, por ejemplo); las
    // huellas SHA-1 de los certificados (20 bytes, públicas) quedan.
    [GeneratedRegex(@"^(?:[0-9A-F]{2}){24,}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexBytes();

    [GeneratedRegex(@"[_\-.\s]+|(?<=[a-z0-9])(?=[A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex WordBoundary();
}
