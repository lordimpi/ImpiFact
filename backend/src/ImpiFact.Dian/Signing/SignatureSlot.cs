using System.Xml;

namespace ImpiFact.Dian.Signing;

/// <summary>
/// Ubica en el texto del XML el lugar reservado para la firma: el ext:ExtensionContent vacío del segundo
/// ext:UBLExtension (ruta /*/ext:UBLExtensions/ext:UBLExtension[2]/ext:ExtensionContent, Anexo Técnico
/// 002, sección 13), o el de la única extensión cuando el documento tiene una sola.
/// </summary>
/// <remarks>
/// ext:UBLExtension es el bloque del XML donde van las extensiones: el primero lleva los datos propios de
/// la DIAN y el segundo, la firma. La firma se inserta directamente en el texto original, en vez de
/// modificar el documento con un parser XML, para que todos los demás bytes queden exactamente como estaban.
/// </remarks>
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

    /// <summary>
    /// Busca el lugar reservado para la firma; falla si no existe o si ya tiene contenido.
    /// </summary>
    /// <remarks>
    /// Un documento con un solo ext:UBLExtension (el AttachedDocument, contenedor electrónico que se entrega
    /// al comprador) no tiene extensiones de la DIAN: si el ext:ExtensionContent de esa única extensión está
    /// vacío, ese es el lugar de la firma, como en la plantilla 89 de ApiDian (la implementación de
    /// referencia en PHP).
    /// </remarks>
    public static SignatureSlot Find(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), XmlSigner.SafeReaderSettings);
        var lineInfo = (IXmlLineInfo)reader;
        var extensionCount = 0;
        SignatureSlot? emptyFirstSlot = null;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != ExtNs)
            {
                continue;
            }

            // Profundidad 0 = raíz, 1 = ext:UBLExtensions, 2 = ext:UBLExtension, 3 = ext:ExtensionContent.
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
            else if (reader is { Depth: 3, LocalName: "ExtensionContent" } && extensionCount == 1)
            {
                var slot = Locate(xml, reader.Name, lineInfo.LineNumber, lineInfo.LinePosition);
                emptyFirstSlot = IsEmpty(reader) ? slot : null;
            }
        }

        if (extensionCount == 1 && emptyFirstSlot is not null)
        {
            return emptyFirstSlot;
        }

        throw new InvalidOperationException(
            $"The document has no {SlotPath} (or a single, empty ext:UBLExtension) to hold the signature.");
    }

    /// <summary>
    /// Devuelve el documento con <paramref name="signatureXml"/> como único contenido del lugar reservado
    /// para la firma.
    /// </summary>
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

        // XmlReader informa línea y columna contando desde 1. Para un elemento, la columna (LinePosition)
        // apunta al primer carácter de su nombre, justo después de '<'.
        var nameStart = lineStart + position - 1;
        if (nameStart < 1 || xml[nameStart - 1] != '<' || string.CompareOrdinal(xml, nameStart, qualifiedName, 0, qualifiedName.Length) != 0)
        {
            throw new InvalidOperationException($"Could not locate {SlotPath} in the document text.");
        }

        var startTagEnd = FindStartTagEnd(xml, nameStart);
        return new SignatureSlot(xml, qualifiedName, startTagEnd, xml[startTagEnd - 1] == '/');
    }

    // Devuelve la posición del '>' que cierra la etiqueta de apertura, sin contar los '>' que aparecen
    // dentro de valores de atributos entre comillas.
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
        if (!IsEmpty(reader))
        {
            throw new InvalidOperationException($"{SlotPath} must be empty before signing.");
        }
    }

    // Lee el elemento actual hasta su cierre e indica si solo tiene espacios en blanco.
    private static bool IsEmpty(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return true;
        }

        var depth = reader.Depth;
        var empty = true;
        while (reader.Read() && reader.Depth > depth)
        {
            empty &= reader.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace;
        }

        return empty;
    }
}
