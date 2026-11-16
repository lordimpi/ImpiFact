using System.Text;

namespace ImpiFact.Dian.Ubl;

/// <summary>
/// Escribe un árbol <see cref="UblElement"/> byte a byte como lo hace DOMDocument::saveXML de PHP con
/// formatOutput activado (la referencia con la que se capturaron las fixtures golden de la DIAN): la
/// declaración XML con standalone="no", indentación de dos espacios, finales de línea LF, elementos
/// vacíos como <c>&lt;x/&gt;</c> sin espacio, caracteres no ASCII como UTF-8 sin escapar y un salto de
/// línea final. El XmlWriter de .NET difiere en varios de estos puntos (por ejemplo, escribe
/// <c>&lt;x /&gt;</c>).
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

    /// <summary>Serializa a bytes UTF-8 sin marca de orden de bytes (BOM).</summary>
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
