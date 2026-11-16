using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ImpiFact.Dian.Signing;

/// <summary>
/// RFC 2253 string form of a distinguished name, as XMLDSig asks for in ds:X509IssuerName:
/// RDNs from last to first, no space after the comma, attribute types without a short name
/// written as dotted OID plus the hex BER value.
/// </summary>
internal static class DistinguishedNameFormatter
{
    // RFC 2253 section 2.3 keywords.
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
        // reversed: true yields the last encoded RDN first, which is the RFC 2253 order.
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

    // RFC 2253 section 2.4.
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
