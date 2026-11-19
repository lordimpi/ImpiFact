using System.Text;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// Respuestas SOAP de la DIAN falsa, con la forma de las muestras de <c>ImpiFact.Dian.Tests</c>
/// (<c>DianResponseSamples</c>): los contratos de datos de WCF (el marco de servicios web de Microsoft que usa la DIAN).
/// </summary>
public static class DianReplies
{
    public const string ApplicationResponseXml =
        "<ApplicationResponse xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:ApplicationResponse-2\" " +
        "xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\">" +
        "<cbc:IssueDate>2026-03-02</cbc:IssueDate><cbc:IssueTime>08:00:01-05:00</cbc:IssueTime></ApplicationResponse>";

    public const string Notification = "Regla: FAK55, Notificación: Correo electrónico no informado";
    public const string Rejection = "Regla: FAD06, Rechazo: Valor del CUFE no está calculado correctamente.";
    public const string Rule90 = "Regla: 90, Rechazo: Documento procesado anteriormente.";
    public const string UnknownTrackId = "TrackId no existe en los registros de la DIAN.";

    /// <summary>El SOAP Fault que devolvió la DIAN de habilitación ante un sobre con firma inválida (fase 1b).</summary>
    public const string ClientFault = """
        <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"><s:Body><s:Fault><s:Code><s:Value>s:Client</s:Value></s:Code><s:Reason><s:Text xml:lang="en-US">The creator of this fault did not specify a Reason.</s:Text></s:Reason></s:Fault></s:Body></s:Envelope>
        """;

    public const string ServiceUnavailablePage = """
        <!DOCTYPE html>
        <html><head><title>Service Unavailable</title></head><body><h2>Service Unavailable</h2></body></html>
        """;

    public static string SendBillSync(bool isValid, string statusCode, params string[] errors) =>
        Envelope("SendBillSync", Result("SendBillSyncResult", isValid, statusCode, errors, withApplicationResponse: true));

    public static string GetStatus(bool isValid, string statusCode, params string[] errors) =>
        Envelope("GetStatus", Result("GetStatusResult", isValid, statusCode, errors, withApplicationResponse: statusCode != "98"));

    /// <summary>Una respuesta de GetStatus que decide pero no trae el ApplicationResponse (XmlBase64Bytes nulo).</summary>
    public static string GetStatusWithoutApplicationResponse(bool isValid, string statusCode) =>
        Envelope("GetStatus", Result("GetStatusResult", isValid, statusCode, [], withApplicationResponse: false));

    public static string GetStatusZip(bool isValid, string statusCode, params string[] errors) =>
        Envelope("GetStatusZip", $"""
            <GetStatusZipResponse xmlns="http://wcf.dian.colombia"><GetStatusZipResult xmlns:b="http://schemas.datacontract.org/2004/07/DianResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:DianResponse>{Fields(isValid, statusCode, errors, statusCode != "98")}</b:DianResponse></GetStatusZipResult></GetStatusZipResponse>
            """);

    public static string SendTestSetAsync(string zipKey) => Envelope("SendTestSetAsync", $"""
        <SendTestSetAsyncResponse xmlns="http://wcf.dian.colombia"><SendTestSetAsyncResult xmlns:b="http://schemas.datacontract.org/2004/07/UploadDocumentResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:ErrorMessageList xmlns:c="http://schemas.datacontract.org/2004/07/XmlParamsResponseTrackId"/><b:ZipKey>{zipKey}</b:ZipKey></SendTestSetAsyncResult></SendTestSetAsyncResponse>
        """);

    private static string Result(string element, bool isValid, string statusCode, string[] errors, bool withApplicationResponse)
    {
        var operation = element[..^"Result".Length];
        return $"""<{operation}Response xmlns="http://wcf.dian.colombia"><{element} xmlns:b="http://schemas.datacontract.org/2004/07/DianResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance">{Fields(isValid, statusCode, errors, withApplicationResponse)}</{element}></{operation}Response>""";
    }

    private static string Fields(bool isValid, string statusCode, string[] errors, bool withApplicationResponse)
    {
        var messages = string.Concat(errors.Select(error => $"<c:string>{error}</c:string>"));
        var applicationResponse = withApplicationResponse
            ? $"<b:XmlBase64Bytes>{Convert.ToBase64String(Encoding.UTF8.GetBytes(ApplicationResponseXml))}</b:XmlBase64Bytes>"
            : "<b:XmlBase64Bytes i:nil=\"true\"/>";
        var description = statusCode switch
        {
            "00" => "Procesado Correctamente.",
            "98" => "En Proceso de Validación",
            _ => "Validación contiene errores en campos mandatorios.",
        };
        return $"""<b:ErrorMessage xmlns:c="http://schemas.microsoft.com/2003/10/Serialization/Arrays">{messages}</b:ErrorMessage><b:IsValid>{(isValid ? "true" : "false")}</b:IsValid><b:StatusCode>{statusCode}</b:StatusCode><b:StatusDescription>{description}</b:StatusDescription><b:StatusMessage i:nil="true"/>{applicationResponse}<b:XmlBytes i:nil="true"/><b:XmlDocumentKey i:nil="true"/><b:XmlFileName i:nil="true"/>""";
    }

    private static string Envelope(string operation, string body) => $"""
        <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:a="http://www.w3.org/2005/08/addressing"><s:Header><a:Action s:mustUnderstand="1">http://wcf.dian.colombia/IWcfDianCustomerServices/{operation}Response</a:Action></s:Header><s:Body>{body}</s:Body></s:Envelope>
        """;
}
