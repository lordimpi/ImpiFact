using System.Globalization;
using System.Text;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// RADIAN answers of the fake DIAN (phase 7a, T6), shaped like the examples of Anexo Técnico 1.9 (sections 7.13, 7.14 and
/// 7.17): SendEventUpdateStatus, GetXmlByDocumentKey and GetStatusEvent, and a received invoice with the fields that
/// ImpiFact reads.
/// </summary>
public static class RadianReplies
{
    /// <summary>NIT of the fictitious seller of the received invoices.</summary>
    public const string SellerNit = "800000002";

    public const string SellerName = "PROVEEDOR DE PRUEBA RADIAN SAS";

    /// <summary>A received invoice addressed to <paramref name="buyerNit"/> with the CUFE <paramref name="cufe"/>.</summary>
    public static string Invoice(string buyerNit, string cufe, string number = "FPRU1001", string typeCode = "01") => $"""
        <?xml version="1.0" encoding="UTF-8" standalone="no"?>
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2" xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2" xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2" xmlns:ext="urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2" xmlns:sts="urn:dian:gov:co:facturaelectronica:Structures-2-1">
          <ext:UBLExtensions><ext:UBLExtension><ext:ExtensionContent><sts:DianExtensions><sts:InvoiceControl><sts:AuthorizedInvoices><sts:Prefix>FPRU</sts:Prefix></sts:AuthorizedInvoices></sts:InvoiceControl></sts:DianExtensions></ext:ExtensionContent></ext:UBLExtension></ext:UBLExtensions>
          <cbc:ProfileExecutionID>2</cbc:ProfileExecutionID>
          <cbc:ID>{number}</cbc:ID>
          <cbc:UUID schemeID="2" schemeName="CUFE-SHA384">{cufe}</cbc:UUID>
          <cbc:IssueDate>2026-01-10</cbc:IssueDate>
          <cbc:IssueTime>09:00:00-05:00</cbc:IssueTime>
          <cbc:InvoiceTypeCode>{typeCode}</cbc:InvoiceTypeCode>
          <cac:AccountingSupplierParty>
            <cbc:AdditionalAccountID>1</cbc:AdditionalAccountID>
            <cac:Party>
              <cac:PartyTaxScheme><cbc:RegistrationName>{SellerName}</cbc:RegistrationName><cbc:CompanyID schemeID="7" schemeName="31">{SellerNit}</cbc:CompanyID><cbc:TaxLevelCode listName="48">O-13</cbc:TaxLevelCode></cac:PartyTaxScheme>
              <cac:Contact><cbc:ElectronicMail>proveedor.radian@example.com</cbc:ElectronicMail></cac:Contact>
            </cac:Party>
          </cac:AccountingSupplierParty>
          <cac:AccountingCustomerParty>
            <cbc:AdditionalAccountID>1</cbc:AdditionalAccountID>
            <cac:Party><cac:PartyTaxScheme><cbc:RegistrationName>COMPRADOR</cbc:RegistrationName><cbc:CompanyID>{buyerNit}</cbc:CompanyID></cac:PartyTaxScheme></cac:Party>
          </cac:AccountingCustomerParty>
          <cac:LegalMonetaryTotal><cbc:LineExtensionAmount currencyID="COP">100000.00</cbc:LineExtensionAmount><cbc:TaxExclusiveAmount currencyID="COP">100000.00</cbc:TaxExclusiveAmount><cbc:TaxInclusiveAmount currencyID="COP">119000.00</cbc:TaxInclusiveAmount><cbc:AllowanceTotalAmount currencyID="COP">0.00</cbc:AllowanceTotalAmount><cbc:PayableAmount currencyID="COP">119000.00</cbc:PayableAmount></cac:LegalMonetaryTotal>
        </Invoice>
        """;

    /// <summary>An AttachedDocument whose first attachment carries <paramref name="invoiceXml"/>, as a seller delivers it.</summary>
    public static string AttachedDocument(string invoiceXml) => $"""
        <?xml version="1.0" encoding="UTF-8" standalone="no"?>
        <AttachedDocument xmlns="urn:oasis:names:specification:ubl:schema:xsd:AttachedDocument-2" xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2" xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
          <cbc:ID>FPRU1001</cbc:ID>
          <cac:Attachment><cac:ExternalReference><cbc:MimeCode>text/xml</cbc:MimeCode><cbc:Description><![CDATA[{invoiceXml}]]></cbc:Description></cac:ExternalReference></cac:Attachment>
        </AttachedDocument>
        """;

    public static string XmlByDocumentKey(string? xml, string code = "100", string message = "Accion completada OK") => Envelope("GetXmlByDocumentKey", $"""
        <GetXmlByDocumentKeyResponse xmlns="http://wcf.dian.colombia"><GetXmlByDocumentKeyResult xmlns:b="http://schemas.datacontract.org/2004/07/EventResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:Code>{code}</b:Code><b:Message>{message}</b:Message>{(xml is null ? "<b:XmlBytesBase64 i:nil=\"true\"/>" : $"<b:XmlBytesBase64>{Convert.ToBase64String(Encoding.UTF8.GetBytes(xml))}</b:XmlBytesBase64>")}</GetXmlByDocumentKeyResult></GetXmlByDocumentKeyResponse>
        """);

    public static string SendEvent(bool isValid, params string[] errors) =>
        Envelope("SendEventUpdateStatus", Result("SendEventUpdateStatus", isValid, isValid ? "00" : "99", errors, applicationResponse: null));

    /// <summary>GetStatusEvent with the events (code and effective date) registered on the invoice.</summary>
    public static string StatusEvents(params (string Code, DateOnly Date)[] events)
    {
        var responses = string.Concat(events.Select(e =>
            $"<cac:DocumentResponse><cac:Response><cbc:ResponseCode>{e.Code}</cbc:ResponseCode><cbc:Description>Evento {e.Code}</cbc:Description>" +
            $"<cbc:EffectiveDate>{e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}</cbc:EffectiveDate><cbc:EffectiveTime>08:00:00</cbc:EffectiveTime></cac:Response>" +
            $"<cac:DocumentReference><cbc:UUID>cude{e.Code}</cbc:UUID></cac:DocumentReference></cac:DocumentResponse>"));
        var applicationResponse =
            "<ApplicationResponse xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:ApplicationResponse-2\" " +
            "xmlns:cac=\"urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2\" " +
            "xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\">" + responses + "</ApplicationResponse>";
        return Envelope("GetStatusEvent", Result("GetStatusEvent", true, "00", [], applicationResponse));
    }

    private static string Result(string operation, bool isValid, string statusCode, string[] errors, string? applicationResponse)
    {
        var messages = string.Concat(errors.Select(error => $"<c:string>{error}</c:string>"));
        var bytes = applicationResponse is null
            ? "<b:XmlBase64Bytes i:nil=\"true\"/>"
            : $"<b:XmlBase64Bytes>{Convert.ToBase64String(Encoding.UTF8.GetBytes(applicationResponse))}</b:XmlBase64Bytes>";
        var description = isValid ? "Procesado Correctamente." : "Validación contiene errores en campos mandatorios.";
        return $"""<{operation}Response xmlns="http://wcf.dian.colombia"><{operation}Result xmlns:b="http://schemas.datacontract.org/2004/07/DianResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:ErrorMessage xmlns:c="http://schemas.microsoft.com/2003/10/Serialization/Arrays">{messages}</b:ErrorMessage><b:IsValid>{(isValid ? "true" : "false")}</b:IsValid><b:StatusCode>{statusCode}</b:StatusCode><b:StatusDescription>{description}</b:StatusDescription><b:StatusMessage i:nil="true"/>{bytes}<b:XmlBytes i:nil="true"/><b:XmlDocumentKey i:nil="true"/><b:XmlFileName i:nil="true"/></{operation}Result></{operation}Response>""";
    }

    private static string Envelope(string operation, string body) => $"""
        <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:a="http://www.w3.org/2005/08/addressing"><s:Header><a:Action s:mustUnderstand="1">http://wcf.dian.colombia/IWcfDianCustomerServices/{operation}Response</a:Action></s:Header><s:Body>{body}</s:Body></s:Envelope>
        """;
}
