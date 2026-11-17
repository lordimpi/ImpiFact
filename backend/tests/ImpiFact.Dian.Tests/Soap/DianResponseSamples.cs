using System.Text;

namespace ImpiFact.Dian.Tests.Soap;

/// <summary>
/// Respuestas SOAP de ejemplo del servicio web de la DIAN.
/// </summary>
/// <remarks>
/// No hay respuestas XML crudas de la DIAN en el repositorio de ApiDian (la implementación de referencia en
/// PHP); su README trae una respuesta real de SendBillSync convertida a JSON. La de SendBillSync se
/// reconstruye a XML a partir de ese ejemplo (mismos campos, valores y espacios de nombres de los contratos
/// de datos de WCF, el marco de servicios web de Microsoft que usa la DIAN). Las demás son ejemplos
/// redactados con la misma forma, porque no hay muestras reales: [verificar] en la fase 1b contra la DIAN.
/// </remarks>
public static class DianResponseSamples
{
    public const string ApplicationResponseXml = "<ApplicationResponse xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:ApplicationResponse-2\"/>";

    public const string DocumentKey =
        "872e91994383cc40de612527df2c50c4811dda9ba126acaaacee953ecd72e74b2eb5d12a7a0d52adacd085cf3469a942";

    public static string ApplicationResponseBase64 => Convert.ToBase64String(Encoding.UTF8.GetBytes(ApplicationResponseXml));

    public static string SendBillSyncAccepted => Envelope("SendBillSync", $"""
        <SendBillSyncResponse xmlns="http://wcf.dian.colombia"><SendBillSyncResult xmlns:b="http://schemas.datacontract.org/2004/07/DianResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:ErrorMessage xmlns:c="http://schemas.microsoft.com/2003/10/Serialization/Arrays"><c:string>Regla: FAK55, Notificación: Correo electrónico no informado</c:string><c:string>Regla: FAJ43b, Notificación: Nombre informado No corresponde al registrado en el RUT con respecto al Nit suminstrado.</c:string></b:ErrorMessage><b:IsValid>true</b:IsValid><b:StatusCode>00</b:StatusCode><b:StatusDescription>Procesado Correctamente.</b:StatusDescription><b:StatusMessage>La Factura electrónica SETP994500239, ha sido autorizada.</b:StatusMessage><b:XmlBase64Bytes>{ApplicationResponseBase64}</b:XmlBase64Bytes><b:XmlBytes i:nil="true"/><b:XmlDocumentKey>{DocumentKey}</b:XmlDocumentKey><b:XmlFileName>fv09018462800002500000166</b:XmlFileName></SendBillSyncResult></SendBillSyncResponse>
        """);

    public static string SendBillSyncRejected => Envelope("SendBillSync", $"""
        <SendBillSyncResponse xmlns="http://wcf.dian.colombia"><SendBillSyncResult xmlns:b="http://schemas.datacontract.org/2004/07/DianResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:ErrorMessage xmlns:c="http://schemas.microsoft.com/2003/10/Serialization/Arrays"><c:string>Regla: FAD06, Rechazo: Valor del CUFE no está calculado correctamente.</c:string><c:string>Regla: FAB05b, Rechazo: Número de factura fuera del rango de numeración autorizado.</c:string></b:ErrorMessage><b:IsValid>false</b:IsValid><b:StatusCode>99</b:StatusCode><b:StatusDescription>Validación contiene errores en campos mandatorios.</b:StatusDescription><b:StatusMessage i:nil="true"/><b:XmlBase64Bytes>{ApplicationResponseBase64}</b:XmlBase64Bytes><b:XmlBytes i:nil="true"/><b:XmlDocumentKey>{DocumentKey}</b:XmlDocumentKey><b:XmlFileName>fv09018462800002500000166</b:XmlFileName></SendBillSyncResult></SendBillSyncResponse>
        """);

    public static string SendTestSetAsyncAccepted => Envelope("SendTestSetAsync", """
        <SendTestSetAsyncResponse xmlns="http://wcf.dian.colombia"><SendTestSetAsyncResult xmlns:b="http://schemas.datacontract.org/2004/07/UploadDocumentResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:ErrorMessageList xmlns:c="http://schemas.datacontract.org/2004/07/XmlParamsResponseTrackId"/><b:ZipKey>3f6e1d52-8a3b-4c1e-9b7a-2d4c5e6f7a8b</b:ZipKey></SendTestSetAsyncResult></SendTestSetAsyncResponse>
        """);

    public static string SendTestSetAsyncWithErrors => Envelope("SendTestSetAsync", """
        <SendTestSetAsyncResponse xmlns="http://wcf.dian.colombia"><SendTestSetAsyncResult xmlns:b="http://schemas.datacontract.org/2004/07/UploadDocumentResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:ErrorMessageList xmlns:c="http://schemas.datacontract.org/2004/07/XmlParamsResponseTrackId"><c:XmlParamsResponseTrackId><c:DocumentKey i:nil="true"/><c:ProcessedMessage>Set de pruebas inexistente o inactivo.</c:ProcessedMessage><c:SenderCode>901846280</c:SenderCode><c:Success>false</c:Success><c:XmlFileName>fv09018462800002500000166</c:XmlFileName></c:XmlParamsResponseTrackId></b:ErrorMessageList><b:ZipKey i:nil="true"/></SendTestSetAsyncResult></SendTestSetAsyncResponse>
        """);

    public static string GetStatusAccepted => Envelope("GetStatus", $"""
        <GetStatusResponse xmlns="http://wcf.dian.colombia"><GetStatusResult xmlns:b="http://schemas.datacontract.org/2004/07/DianResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:ErrorMessage xmlns:c="http://schemas.microsoft.com/2003/10/Serialization/Arrays"/><b:IsValid>true</b:IsValid><b:StatusCode>00</b:StatusCode><b:StatusDescription>Procesado Correctamente.</b:StatusDescription><b:StatusMessage>La Factura electrónica SETP994500239, ha sido autorizada.</b:StatusMessage><b:XmlBase64Bytes>{ApplicationResponseBase64}</b:XmlBase64Bytes><b:XmlBytes i:nil="true"/><b:XmlDocumentKey>{DocumentKey}</b:XmlDocumentKey><b:XmlFileName>fv09018462800002500000166</b:XmlFileName></GetStatusResult></GetStatusResponse>
        """);

    public static string GetStatusZipTwoDocuments => Envelope("GetStatusZip", $"""
        <GetStatusZipResponse xmlns="http://wcf.dian.colombia"><GetStatusZipResult xmlns:b="http://schemas.datacontract.org/2004/07/DianResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:DianResponse><b:ErrorMessage xmlns:c="http://schemas.microsoft.com/2003/10/Serialization/Arrays"/><b:IsValid>true</b:IsValid><b:StatusCode>00</b:StatusCode><b:StatusDescription>Procesado Correctamente.</b:StatusDescription><b:StatusMessage>La Factura electrónica SETP994500239, ha sido autorizada.</b:StatusMessage><b:XmlBase64Bytes>{ApplicationResponseBase64}</b:XmlBase64Bytes><b:XmlBytes i:nil="true"/><b:XmlDocumentKey>{DocumentKey}</b:XmlDocumentKey><b:XmlFileName>fv09018462800002500000166</b:XmlFileName></b:DianResponse><b:DianResponse><b:ErrorMessage xmlns:c="http://schemas.microsoft.com/2003/10/Serialization/Arrays"/><b:IsValid>false</b:IsValid><b:StatusCode>98</b:StatusCode><b:StatusDescription>En Proceso de Validación</b:StatusDescription><b:StatusMessage i:nil="true"/><b:XmlBase64Bytes i:nil="true"/><b:XmlBytes i:nil="true"/><b:XmlDocumentKey i:nil="true"/><b:XmlFileName i:nil="true"/></b:DianResponse></GetStatusZipResult></GetStatusZipResponse>
        """);

    public const string SecurityFault = """
        <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:a="http://www.w3.org/2005/08/addressing"><s:Header><a:Action s:mustUnderstand="1">http://www.w3.org/2005/08/addressing/soap/fault</a:Action></s:Header><s:Body><s:Fault><s:Code><s:Value>s:Sender</s:Value><s:Subcode><s:Value xmlns:a="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd">a:InvalidSecurity</s:Value></s:Subcode></s:Code><s:Reason><s:Text xml:lang="en-US">An error occurred when verifying security for the message.</s:Text></s:Reason></s:Fault></s:Body></s:Envelope>
        """;

    public const string HtmlErrorPage = """
        <!DOCTYPE html>
        <html><head><title>Service Unavailable</title></head><body><h2>Service Unavailable</h2><hr><p>HTTP Error 503. The service is unavailable.</p></body></html>
        """;

    public const string TechnicalKey = "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c";

    /// <summary>
    /// GetNumberingRange con dos rangos autorizados: el del set de pruebas (con su clave técnica) y uno de notas sin
    /// clave. Forma de los contratos de datos <c>NumberRangeResponseList</c> y <c>NumberRangeResponse</c> del servicio.
    /// </summary>
    public static string NumberingRangeTwoRanges => Envelope("GetNumberingRange", $"""
        <GetNumberingRangeResponse xmlns="http://wcf.dian.colombia"><GetNumberingRangeResult xmlns:b="http://schemas.datacontract.org/2004/07/NumberRangeResponseList" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:OperationCode>100</b:OperationCode><b:OperationDescription>Acción completada OK.</b:OperationDescription><b:ResponseList xmlns:c="http://schemas.datacontract.org/2004/07/NumberRangeResponse"><c:NumberRangeResponse><c:ResolutionNumber>18760000001</c:ResolutionNumber><c:ResolutionDate>2019-01-19</c:ResolutionDate><c:Prefix>SETP</c:Prefix><c:FromNumber>990000000</c:FromNumber><c:ToNumber>995000000</c:ToNumber><c:ValidDateFrom>2019-01-19</c:ValidDateFrom><c:ValidDateTo>2030-01-19</c:ValidDateTo><c:TechnicalKey>{TechnicalKey}</c:TechnicalKey></c:NumberRangeResponse><c:NumberRangeResponse><c:ResolutionNumber>18760000002</c:ResolutionNumber><c:ResolutionDate>2024-05-02</c:ResolutionDate><c:Prefix>NC</c:Prefix><c:FromNumber>1</c:FromNumber><c:ToNumber>1000</c:ToNumber><c:ValidDateFrom>2024-05-02</c:ValidDateFrom><c:ValidDateTo>2026-05-02</c:ValidDateTo><c:TechnicalKey i:nil="true"/></c:NumberRangeResponse></b:ResponseList></GetNumberingRangeResult></GetNumberingRangeResponse>
        """);

    /// <summary>GetNumberingRange cuando la DIAN no autoriza la consulta: código de operación sin rangos.</summary>
    public static string NumberingRangeNotAuthorized => Envelope("GetNumberingRange", """
        <GetNumberingRangeResponse xmlns="http://wcf.dian.colombia"><GetNumberingRangeResult xmlns:b="http://schemas.datacontract.org/2004/07/NumberRangeResponseList" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:OperationCode>301</b:OperationCode><b:OperationDescription>Nit del proveedor tecnológico no autorizado.</b:OperationDescription><b:ResponseList i:nil="true" xmlns:c="http://schemas.datacontract.org/2004/07/NumberRangeResponse"/></GetNumberingRangeResult></GetNumberingRangeResponse>
        """);

    /// <summary>
    /// GetAcquirer con un adquiriente registrado: código 200 con su nombre y su correo de recepción. Forma del contrato de
    /// datos del servicio (<c>StatusCode</c>, <c>Message</c>, <c>ReceiverName</c>, <c>ReceiverEmail</c>); el espacio de
    /// nombres del contrato está por confirmar con la DIAN real y el lector no depende de él.
    /// </summary>
    public static string AcquirerFound => Envelope("GetAcquirer", """
        <GetAcquirerResponse xmlns="http://wcf.dian.colombia"><GetAcquirerResult xmlns:b="http://schemas.datacontract.org/2004/07/ExchangeEmailResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:Message>Consulta exitosa.</b:Message><b:ReceiverEmail>facturacion@cliente-prueba.com</b:ReceiverEmail><b:ReceiverName>CLIENTE DE PRUEBA SAS</b:ReceiverName><b:StatusCode>200</b:StatusCode></GetAcquirerResult></GetAcquirerResponse>
        """);

    /// <summary>GetAcquirer cuando la identificación no está registrada: código 404 sin nombre ni correo (así lo trata el PHP).</summary>
    public static string AcquirerNotFound => Envelope("GetAcquirer", """
        <GetAcquirerResponse xmlns="http://wcf.dian.colombia"><GetAcquirerResult xmlns:b="http://schemas.datacontract.org/2004/07/ExchangeEmailResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:Message>No se encontró información para el adquiriente.</b:Message><b:ReceiverEmail i:nil="true"/><b:ReceiverName i:nil="true"/><b:StatusCode>404</b:StatusCode></GetAcquirerResult></GetAcquirerResponse>
        """);

    // Encabezado como el de la respuesta real del README: Action de respuesta y marca de tiempo con Id "_0".
    private static string Envelope(string operation, string body) => $"""
        <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:a="http://www.w3.org/2005/08/addressing" xmlns:u="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd"><s:Header><a:Action s:mustUnderstand="1">http://wcf.dian.colombia/IWcfDianCustomerServices/{operation}Response</a:Action><o:Security s:mustUnderstand="1" xmlns:o="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd"><u:Timestamp u:Id="_0"><u:Created>2025-07-22T17:20:27.320Z</u:Created><u:Expires>2025-07-22T17:25:27.320Z</u:Expires></u:Timestamp></o:Security></s:Header><s:Body>{body}</s:Body></s:Envelope>
        """;
}
