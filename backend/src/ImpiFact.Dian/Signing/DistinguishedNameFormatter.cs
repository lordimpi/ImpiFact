using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ImpiFact.Dian.Signing;

/// <summary>
/// Forma de cadena RFC 2253 de un nombre distintivo, como lo pide XMLDSig en ds:X509IssuerName:
/// RDN del último al primero, sin espacio después de la coma, y los tipos de atributo sin nombre corto
/// escritos como OID con puntos más el valor BER en hexadecimal.
/// </summary>
internal static class DistinguishedNameFormatter
{
    // Palabras clave de la sección 2.3 del RFC 2253.
    private static readonly Dictionary<string, string> ShortNames = new(StringComparer.Ordinal)
    {
        ["2.5.4.3"] = "CN",
        ["2.5.4.7"] = "L",
        ["2.5.4.8"] = "ST",
        ["2.5.4.10"] = "O",
        ["2.5.4.11"] = "OU",
        ["2.5.4.6"] = "C",
        ["2.5.4.9"] = "STREET",
        ["0.9.2342.19200300.100.1.25"] = "DC",
        ["0.9.2342.19200300.100.1.1"] = "UID",
    };

    private static readonly HashSet<UniversalTagNumber> StringTags =
    [
        UniversalTagNumber.UTF8String,
        UniversalTagNumber.PrintableString,
        UniversalTagNumber.IA5String,
        UniversalTagNumber.T61String,
        UniversalTagNumber.BMPString,
        UniversalTagNumber.VisibleString,
        UniversalTagNumber.NumericString,
    ];

    public static string Format(X500DistinguishedName name)
    {
        // reversed: true entrega primero el último RDN codificado, que es el orden del RFC 2253.
        return string.Join(",", name.EnumerateRelativeDistinguishedNames(reversed: true).Select(FormatRdn));
    }

    private static string FormatRdn(X500RelativeDistinguishedName rdn)
    {
        var set = new AsnReader(rdn.RawData, AsnEncodingRules.DER).ReadSetOf();
        var parts = new List<string>();
        while (set.HasData)
        {
            var attribute = set.ReadSequence();
            var oid = attribute.ReadObjectIdentifier();
            var encodedValue = attribute.ReadEncodedValue();
            parts.Add(FormatAttribute(oid, encodedValue));
        }

        return string.Join("+", parts);
    }

    private static string FormatAttribute(string oid, ReadOnlyMemory<byte> encodedValue)
    {
        if (ShortNames.TryGetValue(oid, out var shortName) && TryReadString(encodedValue, out var text))
        {
            return shortName + "=" + Escape(text);
        }

        return oid + "=#" + Convert.ToHexStringLower(encodedValue.Span);
    }

    private static bool TryReadString(ReadOnlyMemory<byte> encodedValue, out string text)
    {
        var reader = new AsnReader(encodedValue, AsnEncodingRules.BER);
        var tag = reader.PeekTag();
        if (tag.TagClass == TagClass.Universal && StringTags.Contains((UniversalTagNumber)tag.TagValue))
        {
            text = reader.ReadCharacterString((UniversalTagNumber)tag.TagValue);
            return true;
        }

        text = string.Empty;
        return false;
    }

    // Sección 2.4 del RFC 2253.
    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var needsEscape = c is ',' or '+' or '"' or '\\' or '<' or '>' or ';'
                || (i == 0 && c is '#' or ' ')
                || (i == value.Length - 1 && c == ' ');
            if (needsEscape)
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
