using System.Globalization;
using System.Xml;

namespace ImpiFact.Dian.Radian;

/// <summary>
/// An event the DIAN has registered on an invoice, as GetStatusEvent lists it (Anexo Técnico 1.9, section 7.17), with the
/// fields ApiDian reads (php-rules 16.6, item 6): the code, the event number, its description, the date and time it took
/// effect and its CUDE. Fields the answer does not bring are null.
/// </summary>
public sealed record DianRegisteredEvent(
    string Code, string? Number, string? Description, DateOnly? EffectiveDate, TimeOnly? EffectiveTime, string? Cude);

/// <summary>Reads the events of the ApplicationResponse that GetStatusEvent returns, one per cac:DocumentResponse.</summary>
public static class RadianEventStatusReader
{
    private const string CacNs = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private const string CbcNs = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    /// <returns>The events in document order; empty when the XML cannot be read or lists none.</returns>
    public static IReadOnlyList<DianRegisteredEvent> Read(byte[] applicationResponse)
    {
        ArgumentNullException.ThrowIfNull(applicationResponse);
        XmlDocument doc;
        try
        {
            doc = SafeXml.Load(applicationResponse);
        }
        catch (XmlException)
        {
            return [];
        }

        var events = new List<DianRegisteredEvent>();
        foreach (var documentResponse in doc.GetElementsByTagName("DocumentResponse", CacNs).OfType<XmlElement>())
        {
            var response = Child(documentResponse, CacNs, "Response");
            var code = Text(response, "ResponseCode");
            if (code is null)
            {
                continue;
            }

            events.Add(new DianRegisteredEvent(
                code,
                Text(response, "ReferenceID"),
                Text(response, "Description"),
                Date(Text(response, "EffectiveDate")),
                Time(Text(response, "EffectiveTime")),
                Text(Child(documentResponse, CacNs, "DocumentReference"), "UUID")));
        }

        return events;
    }

    private static XmlElement? Child(XmlElement? parent, string ns, string localName) =>
        parent?.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == localName && e.NamespaceURI == ns);

    private static string? Text(XmlElement? parent, string localName) =>
        Child(parent, CbcNs, localName)?.InnerText.Trim() is { Length: > 0 } text ? text : null;

    private static DateOnly? Date(string? text) =>
        text is { Length: >= 10 } && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static TimeOnly? Time(string? text) =>
        text is { Length: >= 8 } && TimeOnly.TryParseExact(text[..8], "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;
}

/// <summary>Loads XML without DTD processing nor external resources (avoids XML entity attacks).</summary>
internal static class SafeXml
{
    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    public static XmlDocument Load(byte[] xml)
    {
        using var stream = new MemoryStream(xml, writable: false);
        using var reader = XmlReader.Create(stream, Settings);
        var doc = new XmlDocument { XmlResolver = null };
        doc.Load(reader);
        return doc;
    }

    public static XmlDocument Load(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), Settings);
        var doc = new XmlDocument { XmlResolver = null };
        doc.Load(reader);
        return doc;
    }
}
