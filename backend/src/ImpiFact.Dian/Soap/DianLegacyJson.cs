using System.Text.Json.Nodes;
using System.Xml;
using ImpiFact.Dian.Signing;

namespace ImpiFact.Dian.Soap;

/// <summary>
/// Convierte una respuesta del servicio web de la DIAN (la autoridad tributaria de Colombia) en el objeto
/// <c>ResponseDian</c> que devuelve ApiDian (la implementación de referencia en PHP) en sus consultas: el sobre SOAP
/// (protocolo de mensajes XML de servicios web) completo como JSON, desde <c>Envelope</c>.
/// </summary>
/// <remarks>
/// <para>
/// Reglas, las mismas que sigue el PHP al convertir la respuesta: cada elemento es un objeto con sus hijos por nombre
/// sin prefijo de espacio de nombres; sus atributos van en <c>_attributes</c> (sin prefijo, y sin las declaraciones
/// <c>xmlns</c>); un elemento cuyo único contenido es texto es ese texto, o un objeto con <c>_attributes</c> y
/// <c>_value</c> si además tiene atributos; un elemento vacío es un objeto vacío; y los hijos con el mismo nombre
/// forman una lista en el orden del XML. Los valores son siempre texto, como en el PHP.
/// </para>
/// <para>
/// Diferencia con el PHP: el PHP quita solo el último carácter del prefijo y los dos puntos (con un prefijo de una
/// letra, como los de la DIAN, es lo mismo); aquí se usa el nombre local completo. Los espacios en blanco entre
/// elementos se descartan.
/// </para>
/// </remarks>
public static class DianLegacyJson
{
    private const string AttributesKey = "_attributes";
    private const string ValueKey = "_value";
    private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";

    /// <summary>El objeto de <paramref name="xml"/>; nulo si no es un XML bien formado o trae una declaración de tipo.</summary>
    public static JsonObject? FromXml(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var doc = new XmlDocument { PreserveWhitespace = false, XmlResolver = null };
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), XmlSigner.SafeReaderSettings);
            doc.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        var root = doc.DocumentElement!;
        return new JsonObject { [root.LocalName] = Convert(root) };
    }

    private static JsonNode Convert(XmlElement element)
    {
        var result = new JsonObject();
        var attributes = new JsonObject();
        foreach (XmlAttribute attribute in element.Attributes)
        {
            if (attribute.NamespaceURI != XmlnsNamespace)
            {
                attributes[attribute.LocalName] = attribute.Value;
            }
        }

        if (attributes.Count > 0)
        {
            result[AttributesKey] = attributes;
        }

        var children = element.ChildNodes.Cast<XmlNode>().Where(node => node is XmlElement or XmlText or XmlCDataSection).ToList();
        if (children is [XmlCharacterData text])
        {
            if (result.Count == 0)
            {
                return JsonValue.Create(text.Value)!;
            }

            result[ValueKey] = text.Value;
            return result;
        }

        foreach (var child in children)
        {
            var (name, value) = child is XmlElement childElement
                ? (childElement.LocalName, Convert(childElement))
                : ("#text", (JsonNode)JsonValue.Create(child.Value)!);
            if (!result.TryGetPropertyValue(name, out var existing))
            {
                result[name] = value;
            }
            else if (existing is JsonArray list)
            {
                // Un hijo convertido nunca es una lista: las listas solo salen de los hijos repetidos.
                list.Add(value);
            }
            else
            {
                // Se reemplaza en su lugar, así el nombre conserva la posición del primer hijo, como en el PHP.
                result[name] = null;
                result[name] = new JsonArray(existing, value);
            }
        }

        return result;
    }
}
