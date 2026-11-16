using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ImpiFact.Dian.Signing;

/// <summary>
/// Convierte el nombre del emisor de un certificado (por ejemplo "CN=..., O=..., C=CO") en el texto que va
/// en ds:X509IssuerName, con el formato estándar de texto para nombres de certificados (RFC 2253) que pide
/// el estándar de firmas XML (XMLDSig).
/// </summary>
/// <remarks>
/// Reglas: cada parte del nombre del emisor (CN=..., O=..., C=...) se escribe de la última a la primera,
/// según el orden en que están guardadas en el certificado, separadas por una coma sin espacio. Un tipo de
/// parte sin abreviatura conocida se escribe con su identificador numérico (OID, por ejemplo 2.5.4.5),
/// seguido de "=#" y los bytes del valor en hexadecimal, tal como están codificados en el certificado.
/// </remarks>
internal static class DistinguishedNameFormatter
{
    // Abreviaturas (CN, O, C...) que RFC 2253, sección 2.3, permite escribir en lugar del identificador
    // numérico (OID) de cada tipo de parte del nombre.
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

    // Tipos de valor que son texto y se pueden escribir como tal; cualquier otro se escribe en hexadecimal.
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
        // reversed: true entrega primero la última parte guardada en el certificado, que es el orden que pide RFC 2253.
        return string.Join(",", name.EnumerateRelativeDistinguishedNames(reversed: true).Select(FormatRdn));
    }

    // Una parte del nombre puede tener varios atributos (por ejemplo CN=...+UID=...); se unen con '+'.
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

    // Antepone '\' a los caracteres especiales del formato (RFC 2253, sección 2.4): , + " \ < > ; en
    // cualquier posición, '#' o espacio al inicio, y espacio al final.
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
