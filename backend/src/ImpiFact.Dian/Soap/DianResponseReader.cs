using System.Globalization;
using System.Xml;
using ImpiFact.Dian.Signing;

namespace ImpiFact.Dian.Soap;

/// <summary>
/// Interpreta el cuerpo de una respuesta del servicio web de la DIAN (la autoridad tributaria de Colombia) a
/// SendBillSync, SendTestSetAsync, GetStatus, GetStatusZip y GetNumberingRange.
/// </summary>
/// <remarks>
/// <para>
/// La respuesta es un sobre SOAP (protocolo de mensajes XML de servicios web). El servicio está hecho con WCF
/// (el marco de servicios web de Microsoft), que escribe los resultados con espacios de nombres de sus
/// contratos de datos (por ejemplo <c>http://schemas.datacontract.org/2004/07/DianResponse</c> con el prefijo
/// <c>b:</c>). Este lector busca los elementos solo por su nombre local, sin mirar el espacio de nombres ni el
/// prefijo, para no depender de esos detalles.
/// </para>
/// <para>
/// Nunca lanza excepciones por el contenido de la respuesta: un error SOAP (soap:Fault), un cuerpo que no es
/// XML (por ejemplo una página HTML) y un XML sin resultado conocido son resultados distintos de
/// <see cref="DianSoapResponse"/>.
/// </para>
/// </remarks>
public static class DianResponseReader
{
    private const string XsiNs = "http://www.w3.org/2001/XMLSchema-instance";
    private const string ResponseSuffix = "Response";
    private const string StatusZipOperation = "GetStatusZip";
    private const string NumberingRangeOperation = "GetNumberingRange";

    private static readonly HashSet<string> KnownOperations =
        new(["SendBillSync", "SendTestSetAsync", "GetStatus", StatusZipOperation, NumberingRangeOperation], StringComparer.Ordinal);

    /// <summary>Interpreta <paramref name="body"/>, el cuerpo HTTP de la respuesta de la DIAN.</summary>
    public static DianSoapResponse Read(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(body))
        {
            return new DianServiceUnavailableResponse(body);
        }

        XmlDocument doc;
        try
        {
            doc = Load(body);
        }
        catch (XmlException)
        {
            return new DianServiceUnavailableResponse(body);
        }

        var root = doc.DocumentElement!;
        if (root.LocalName.Equals("html", StringComparison.OrdinalIgnoreCase))
        {
            return new DianServiceUnavailableResponse(body);
        }

        var content = root.LocalName == "Envelope" ? Child(Child(root, "Body"), null) : null;
        if (content is null)
        {
            return new DianUnrecognizedResponse(body);
        }

        if (content.LocalName == "Fault")
        {
            return ReadFault(content);
        }

        var operation = content.LocalName.EndsWith(ResponseSuffix, StringComparison.Ordinal)
            ? content.LocalName[..^ResponseSuffix.Length]
            : string.Empty;
        var result = KnownOperations.Contains(operation) ? Child(content, operation + "Result") : null;
        if (result is null)
        {
            return new DianUnrecognizedResponse(body);
        }

        if (operation == NumberingRangeOperation)
        {
            try
            {
                return ReadNumberingRange(result);
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                return new DianUnrecognizedResponse(body);
            }
        }

        // GetStatusZip trae un elemento DianResponse por documento; las demás operaciones traen el resultado directo.
        var items = operation == StatusZipOperation
            ? Children(result).Where(e => e.LocalName == "DianResponse")
            : [result];
        try
        {
            return new DianResultResponse(operation, items.Select(ReadResult).ToList());
        }
        catch (FormatException)
        {
            return new DianUnrecognizedResponse(body);
        }
    }

    private static DianResult ReadResult(XmlElement result)
    {
        var isValid = Text(result, "IsValid");
        var xmlBase64 = Text(result, "XmlBase64Bytes");

        // ErrorMessage: lista de textos (elementos "string"). ErrorMessageList (SendTestSetAsync): un
        // XmlParamsResponseTrackId por documento, con su texto en ProcessedMessage.
        var errors = Children(Child(result, "ErrorMessage")).Select(e => e.InnerText)
            .Concat(Children(Child(result, "ErrorMessageList")).Select(e => Text(e, "ProcessedMessage")).OfType<string>())
            .ToList();

        return new DianResult(
            IsValid: isValid is null ? null : XmlConvert.ToBoolean(isValid),
            StatusCode: Text(result, "StatusCode"),
            StatusDescription: Text(result, "StatusDescription"),
            StatusMessage: Text(result, "StatusMessage"),
            ErrorMessages: errors,
            XmlDocumentKey: Text(result, "XmlDocumentKey"),
            XmlFileName: Text(result, "XmlFileName"),
            XmlBase64Bytes: xmlBase64 is null ? null : Convert.FromBase64String(xmlBase64),
            ZipKey: Text(result, "ZipKey"));
    }

    // GetNumberingRange: OperationCode, OperationDescription y ResponseList con un NumberRangeResponse por rango.
    private static DianNumberingRangeResponse ReadNumberingRange(XmlElement result) => new(
        Text(result, "OperationCode"),
        Text(result, "OperationDescription"),
        Children(Child(result, "ResponseList"))
            .Where(e => e.LocalName == "NumberRangeResponse")
            .Select(range => new DianNumberingRange(
                ResolutionNumber: Text(range, "ResolutionNumber"),
                ResolutionDate: Date(Text(range, "ResolutionDate")),
                Prefix: Text(range, "Prefix"),
                FromNumber: Number(Text(range, "FromNumber")),
                ToNumber: Number(Text(range, "ToNumber")),
                ValidDateFrom: Date(Text(range, "ValidDateFrom")),
                ValidDateTo: Date(Text(range, "ValidDateTo")),
                TechnicalKey: Text(range, "TechnicalKey")))
            .ToList());

    private static long? Number(string? text) =>
        text is null ? null : long.Parse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture);

    /// <summary>Fecha <c>yyyy-MM-dd</c>; si trae hora (<c>yyyy-MM-ddTHH:mm:ss</c>), se toma solo el día.</summary>
    private static DateOnly? Date(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var day = text.Trim();
        return DateOnly.ParseExact(day.Length > 10 && day[10] == 'T' ? day[..10] : day, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    // SOAP 1.2: Code/Value, Code/Subcode/Value y Reason/Text. Si viniera en SOAP 1.1: faultcode y faultstring.
    private static DianSoapFaultResponse ReadFault(XmlElement fault)
    {
        var code = Child(fault, "Code");
        return new DianSoapFaultResponse(
            Code: Text(code, "Value") ?? Text(fault, "faultcode") ?? string.Empty,
            Subcode: Text(Child(code, "Subcode"), "Value"),
            Reason: Text(Child(fault, "Reason"), "Text") ?? Text(fault, "faultstring") ?? string.Empty);
    }

    private static XmlDocument Load(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), XmlSigner.SafeReaderSettings);
        doc.Load(reader);
        return doc;
    }

    private static IEnumerable<XmlElement> Children(XmlElement? parent) =>
        parent is null ? [] : parent.ChildNodes.OfType<XmlElement>();

    /// <summary>Primer hijo con ese nombre local, o el primer hijo si <paramref name="localName"/> es nulo.</summary>
    private static XmlElement? Child(XmlElement? parent, string? localName) =>
        Children(parent).FirstOrDefault(e => localName is null || e.LocalName == localName);

    /// <summary>Texto de un hijo, o nulo si falta, está vacío o viene marcado como nulo (<c>i:nil="true"</c>).</summary>
    private static string? Text(XmlElement? parent, string localName)
    {
        var element = Child(parent, localName);
        if (element is null || element.GetAttribute("nil", XsiNs) == "true" || string.IsNullOrWhiteSpace(element.InnerText))
        {
            return null;
        }

        return element.InnerText;
    }
}
