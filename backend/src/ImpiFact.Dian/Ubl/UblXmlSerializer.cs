using System.Text;

namespace ImpiFact.Dian.Ubl;

/// <summary>
/// Writes a <see cref="UblElement"/> tree byte for byte as PHP's DOMDocument::saveXML does with
/// formatOutput on (the reference the DIAN golden fixtures were captured with): the XML
/// declaration with standalone="no", two-space indentation, LF line ends, empty elements as
/// <c>&lt;x/&gt;</c> without a space, non-ASCII characters as raw UTF-8 and a final newline.
/// .NET's XmlWriter differs on several of these points (for example it writes <c>&lt;x /&gt;</c>).
/// </summary>
public static class UblXmlSerializer
{
    public const string Declaration = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>";

    private const string Indent = "  ";

    public static string Serialize(UblElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var builder = new StringBuilder(16 * 1024);
        builder.Append(Declaration).Append('\n');
        Write(builder, root, 0);
        return builder.ToString();
    }

    /// <summary>Serializes to UTF-8 bytes without a byte order mark.</summary>
    public static byte[] SerializeToUtf8(UblElement root) => new UTF8Encoding(false).GetBytes(Serialize(root));

    private static void Write(StringBuilder builder, UblElement element, int depth)
    {
        for (var i = 0; i < depth; i++)
        {
            builder.Append(Indent);
        }

        builder.Append('<').Append(element.Name);
        foreach (var attribute in element.Attributes)
        {
            builder.Append(' ').Append(attribute.Key).Append("=\"");
            EscapeAttribute(builder, attribute.Value);
            builder.Append('"');
        }

        if (element.Children.Count > 0)
        {
            builder.Append(">\n");
            foreach (var child in element.Children)
            {
                Write(builder, child, depth + 1);
            }

            for (var i = 0; i < depth; i++)
            {
                builder.Append(Indent);
            }

            builder.Append("</").Append(element.Name).Append(">\n");
        }
        else if (!string.IsNullOrEmpty(element.Text))
        {
            builder.Append('>');
            EscapeText(builder, element.Text);
            builder.Append("</").Append(element.Name).Append(">\n");
        }
        else
        {
            builder.Append("/>\n");
        }
    }

    private static void EscapeText(StringBuilder builder, string text)
    {
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '\r': builder.Append("&#13;"); break;
                default: builder.Append(c); break;
            }
        }
    }

    private static void EscapeAttribute(StringBuilder builder, string value)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '"': builder.Append("&quot;"); break;
                case '\n': builder.Append("&#10;"); break;
                case '\r': builder.Append("&#13;"); break;
                case '\t': builder.Append("&#9;"); break;
                default: builder.Append(c); break;
            }
        }
    }
}
