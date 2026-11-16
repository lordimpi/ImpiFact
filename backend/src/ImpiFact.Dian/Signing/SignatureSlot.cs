using System.Xml;

namespace ImpiFact.Dian.Signing;

/// <summary>
/// Posición en el texto de /*/ext:UBLExtensions/ext:UBLExtension[2]/ext:ExtensionContent (Anexo
/// Técnico 002, sección 13), para que la firma se inserte en el texto original y todos los demás bytes
/// del documento queden como estaban.
/// </summary>
internal sealed class SignatureSlot
{
    private const string ExtNs = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";
    private const string SlotPath = "ext:UBLExtensions/ext:UBLExtension[2]/ext:ExtensionContent";

    private readonly string _xml;
    private readonly string _qualifiedName;
    private readonly int _startTagEnd;
    private readonly bool _selfClosing;

    private SignatureSlot(string xml, string qualifiedName, int startTagEnd, bool selfClosing)
    {
        _xml = xml;
        _qualifiedName = qualifiedName;
        _startTagEnd = startTagEnd;
        _selfClosing = selfClosing;
    }

    public static SignatureSlot Find(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), XmlSigner.SafeReaderSettings);
        var lineInfo = (IXmlLineInfo)reader;
        var extensionCount = 0;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != ExtNs)
            {
                continue;
            }

            if (reader is { Depth: 2, LocalName: "UBLExtension" })
            {
                extensionCount++;
            }
            else if (reader is { Depth: 3, LocalName: "ExtensionContent" } && extensionCount == 2)
            {
                var slot = Locate(xml, reader.Name, lineInfo.LineNumber, lineInfo.LinePosition);
                EnsureEmpty(reader);
                return slot;
            }
        }

        throw new InvalidOperationException($"The document has no {SlotPath} to hold the signature.");
    }

    /// <summary>El documento con <paramref name="signatureXml"/> como único contenido del espacio.</summary>
    public string Insert(string signatureXml)
    {
        if (_selfClosing)
        {
            // "<ext:ExtensionContent/>" se convierte en "<ext:ExtensionContent>firma</ext:ExtensionContent>".
            return _xml[..(_startTagEnd - 1)] + ">" + signatureXml + "</" + _qualifiedName + ">" + _xml[(_startTagEnd + 1)..];
        }

        return _xml.Insert(_startTagEnd + 1, signatureXml);
    }

    private static SignatureSlot Locate(string xml, string qualifiedName, int line, int position)
    {
        var lineStart = 0;
        for (var current = 1; current < line; current++)
        {
            lineStart = xml.IndexOf('\n', lineStart) + 1;
        }

        // Para un elemento, LinePosition apunta al primer carácter de su nombre.
        var nameStart = lineStart + position - 1;
        if (nameStart < 1 || xml[nameStart - 1] != '<' || string.CompareOrdinal(xml, nameStart, qualifiedName, 0, qualifiedName.Length) != 0)
        {
            throw new InvalidOperationException($"Could not locate {SlotPath} in the document text.");
        }

        var startTagEnd = FindStartTagEnd(xml, nameStart);
        return new SignatureSlot(xml, qualifiedName, startTagEnd, xml[startTagEnd - 1] == '/');
    }

    private static int FindStartTagEnd(string xml, int from)
    {
        var quote = '\0';
        for (var i = from; i < xml.Length; i++)
        {
            var c = xml[i];
            if (quote != '\0')
            {
                quote = c == quote ? '\0' : quote;
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Unterminated start tag for {SlotPath}.");
    }

    private static void EnsureEmpty(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return;
        }

        var depth = reader.Depth;
        while (reader.Read() && reader.Depth > depth)
        {
            if (reader.NodeType is not (XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace))
            {
                throw new InvalidOperationException($"{SlotPath} must be empty before signing.");
            }
        }
    }
}
