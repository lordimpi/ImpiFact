using System.Text;
using ImpiFact.Dian.Radian;
using ImpiFact.Dian.Soap;

namespace ImpiFact.Dian.Tests.Radian;

/// <summary>
/// The DIAN side of RADIAN (phase 7a, T6): the SOAP operations SendEventUpdateStatus, GetXmlByDocumentKey and
/// GetStatusEvent of Anexo Técnico 1.9 (sections 7.13, 7.14 and 7.17), the name of the event file and the reading of a
/// received invoice. The response samples follow the examples of the annex.
/// </summary>
public sealed class RadianDianTests
{
    private const string Cufe =
        "83c3ba93981ccf3d79c246498209c7762962faccc62b1328e3be207da45569b7dac74cac108cae80addd7e2e4c4e9f9e";

    [Test]
    public void Send_event_update_status_carries_only_the_zip()
    {
        var request = new SendEventUpdateStatusRequest("UEsDBA==");

        Assert.Multiple(() =>
        {
            Assert.That(request.OperationName, Is.EqualTo("SendEventUpdateStatus"));
            Assert.That(request.Action, Is.EqualTo("http://wcf.dian.colombia/IWcfDianCustomerServices/SendEventUpdateStatus"));
            Assert.That(new GetXmlByDocumentKeyRequest(Cufe).OperationName, Is.EqualTo("GetXmlByDocumentKey"));
            Assert.That(new GetStatusEventRequest(Cufe).OperationName, Is.EqualTo("GetStatusEvent"));
        });
    }

    [Test]
    public void A_send_event_update_status_answer_is_a_result_with_its_rules()
    {
        var response = DianResponseReader.Read(Envelope("SendEventUpdateStatus", Result(
            "SendEventUpdateStatus", isValid: false, "99", "Regla: LGC01, Rechazo: Evento registrado previamente")));

        var result = ((DianResultResponse)response).Results.Single();
        Assert.Multiple(() =>
        {
            Assert.That(((DianResultResponse)response).OperationName, Is.EqualTo("SendEventUpdateStatus"));
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo("99"));
            Assert.That(result.ErrorMessages, Is.EqualTo(new[] { "Regla: LGC01, Rechazo: Evento registrado previamente" }));
        });
    }

    [Test]
    public void A_found_xml_by_document_key_brings_the_document_bytes()
    {
        var invoice = Encoding.UTF8.GetBytes("<Invoice/>");

        var response = DianResponseReader.Read(XmlByDocumentKey("100", "Accion completada OK", Convert.ToBase64String(invoice)));

        Assert.That(response, Is.EqualTo(new DianXmlDocumentResponse("100", "Accion completada OK", invoice)).Using<DianXmlDocumentResponse>(SameXml));
    }

    [Test]
    public void A_missing_xml_by_document_key_brings_its_code_without_bytes()
    {
        var response = (DianXmlDocumentResponse)DianResponseReader.Read(XmlByDocumentKey("205", "XML no encontrado", xmlBase64: null));

        Assert.Multiple(() =>
        {
            Assert.That(response.Code, Is.EqualTo("205"));
            Assert.That(response.Message, Is.EqualTo("XML no encontrado"));
            Assert.That(response.Xml, Is.Null);
        });
    }

    [Test]
    public void The_events_of_a_get_status_event_answer_are_read_in_order()
    {
        var applicationResponse = """
            <ApplicationResponse xmlns="urn:oasis:names:specification:ubl:schema:xsd:ApplicationResponse-2" xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2" xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
              <cac:DocumentResponse><cac:Response><cbc:ReferenceID>800000002FPRU1001030</cbc:ReferenceID><cbc:ResponseCode>030</cbc:ResponseCode><cbc:Description>Acuse de recibo</cbc:Description><cbc:EffectiveDate>2026-11-14</cbc:EffectiveDate><cbc:EffectiveTime>14:00:00</cbc:EffectiveTime></cac:Response><cac:DocumentReference><cbc:UUID>aaa</cbc:UUID></cac:DocumentReference></cac:DocumentResponse>
              <cac:DocumentResponse><cac:Response><cbc:ResponseCode>032</cbc:ResponseCode><cbc:EffectiveDate>2026-11-16</cbc:EffectiveDate></cac:Response><cac:DocumentReference><cbc:UUID>bbb</cbc:UUID></cac:DocumentReference></cac:DocumentResponse>
            </ApplicationResponse>
            """;

        var events = RadianEventStatusReader.Read(Encoding.UTF8.GetBytes(applicationResponse));

        Assert.That(events, Is.EqualTo(new[]
        {
            new DianRegisteredEvent("030", "800000002FPRU1001030", "Acuse de recibo", new DateOnly(2026, 11, 14), new TimeOnly(14, 0), "aaa"),
            new DianRegisteredEvent("032", null, null, new DateOnly(2026, 11, 16), null, "bbb"),
        }));
    }

    [Test]
    public void A_get_status_event_answer_is_a_result()
    {
        var response = DianResponseReader.Read(Envelope("GetStatusEvent", Result("GetStatusEvent", isValid: true, "00")));

        Assert.That(((DianResultResponse)response).Results.Single().IsValid, Is.True);
    }

    [Test]
    public void The_event_file_name_follows_the_php_format_without_repeating_the_prefix()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DianFileNames.EventXmlEntry("030", "800000002", 2026, "FPRU1001"), Is.EqualTo("ar030080000000200026FPRU1001.xml"));
            Assert.That(DianFileNames.EventZip("030", "800000002", 2026, "FPRU1001"), Is.EqualTo("z030080000000200026FPRU1001.zip"));
            Assert.That(DianFileNames.EventXmlEntry("034", "900000001", 2026, "1"), Is.EqualTo("ar034090000000100026" + "00000001.xml"));
            Assert.That(DianFileNames.EventXmlEntry("031", "900000001", 2026, "SE-TP/12"), Is.EqualTo("ar03109000000010002600SETP12.xml"),
                "Only letters and digits of the number go in the name, padded on the left like the PHP.");
        });
    }

    [Test]
    public void A_received_invoice_is_read_from_its_xml()
    {
        var xml = File.ReadAllText(GoldenPath("invoice", "final-consumer", "expected.xml"));

        var invoice = ReceivedInvoiceReader.Read(xml);

        Assert.That(invoice, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(invoice!.SupplierNit, Is.EqualTo("900000001"));
            Assert.That(invoice.SupplierDv, Is.EqualTo("2"), "Recalculated, as the PHP does.");
            Assert.That(invoice.SupplierName, Is.EqualTo("EMPRESA DE PRUEBA GOLDEN SAS"));
            Assert.That(invoice.SupplierOrganizationTypeCode, Is.EqualTo("1"));
            Assert.That(invoice.SupplierRegimeCode, Is.EqualTo("48"));
            Assert.That(invoice.CustomerNit, Is.EqualTo("222222222222"));
            Assert.That(invoice.InvoiceTypeCode, Is.EqualTo("01"));
            Assert.That(invoice.Prefix, Is.EqualTo("SETP"));
            Assert.That(invoice.Number, Is.EqualTo("SETP990000010"));
            Assert.That(invoice.Cufe, Is.EqualTo("8d25fa61fbecd2ac90cd907c3714e24beb114733f2e14238380206056d7025f6f24bca74cda90aeb949c439b9c2afe0b"));
            Assert.That(invoice.IssueDate, Is.EqualTo(new DateOnly(2026, 11, 14)));
            Assert.That(invoice.Environment, Is.EqualTo(2));
            Assert.That(invoice.LineExtensionAmount, Is.EqualTo(84033.61m));
            Assert.That(invoice.TaxExclusiveAmount, Is.EqualTo(84033.61m));
            Assert.That(invoice.TaxInclusiveAmount, Is.EqualTo(100000.00m));
            Assert.That(invoice.AllowanceTotalAmount, Is.EqualTo(0m));
            Assert.That(invoice.PayableAmount, Is.EqualTo(100000.00m));
        });
    }

    [Test]
    public void A_received_invoice_is_read_from_the_attached_document_that_carries_it()
    {
        var attached = File.ReadAllText(GoldenPath("attached-document", "invoice", "expected-attached-document.xml"));

        var invoice = ReceivedInvoiceReader.Read(attached);

        Assert.That(invoice?.Number, Is.EqualTo("SETP990000002"));
    }

    [Test]
    public void A_document_that_is_not_an_invoice_is_not_read()
    {
        var note = File.ReadAllText(Directory.EnumerateFiles(GoldenRoot("credit-note"), "expected.xml", SearchOption.AllDirectories).First());

        Assert.Multiple(() =>
        {
            Assert.That(ReceivedInvoiceReader.Read(note), Is.Null);
            Assert.That(ReceivedInvoiceReader.Read("not xml"), Is.Null);
            Assert.That(ReceivedInvoiceReader.Read("<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///c:/x\">]><Invoice>&e;</Invoice>"), Is.Null);
        });
    }

    [Test]
    public void The_check_digit_follows_the_dian_algorithm()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NitCheckDigit.Compute("900000001"), Is.EqualTo(2));
            Assert.That(NitCheckDigit.Compute("800000002"), Is.EqualTo(7));
            Assert.That(NitCheckDigit.Compute("800197268"), Is.EqualTo(4));
        });
    }

    private static bool SameXml(DianXmlDocumentResponse a, DianXmlDocumentResponse b) =>
        a.Code == b.Code && a.Message == b.Message && (a.Xml ?? []).AsSpan().SequenceEqual(b.Xml ?? []);

    private static string XmlByDocumentKey(string code, string message, string? xmlBase64) => Envelope("GetXmlByDocumentKey", $"""
        <GetXmlByDocumentKeyResponse xmlns="http://wcf.dian.colombia"><GetXmlByDocumentKeyResult xmlns:b="http://schemas.datacontract.org/2004/07/EventResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:Code>{code}</b:Code><b:Message>{message}</b:Message>{(xmlBase64 is null ? "<b:XmlBytesBase64 i:nil=\"true\"/>" : $"<b:XmlBytesBase64>{xmlBase64}</b:XmlBytesBase64>")}</GetXmlByDocumentKeyResult></GetXmlByDocumentKeyResponse>
        """);

    private static string Result(string operation, bool isValid, string statusCode, params string[] errors)
    {
        var messages = string.Concat(errors.Select(error => $"<c:string>{error}</c:string>"));
        return $"""<{operation}Response xmlns="http://wcf.dian.colombia"><{operation}Result xmlns:b="http://schemas.datacontract.org/2004/07/DianResponse" xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><b:ErrorMessage xmlns:c="http://schemas.microsoft.com/2003/10/Serialization/Arrays">{messages}</b:ErrorMessage><b:IsValid>{(isValid ? "true" : "false")}</b:IsValid><b:StatusCode>{statusCode}</b:StatusCode><b:StatusDescription>Procesado Correctamente.</b:StatusDescription><b:StatusMessage i:nil="true"/><b:XmlBase64Bytes i:nil="true"/><b:XmlBytes i:nil="true"/><b:XmlDocumentKey>{Cufe}</b:XmlDocumentKey><b:XmlFileName i:nil="true"/></{operation}Result></{operation}Response>""";
    }

    private static string Envelope(string operation, string body) => $"""
        <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:a="http://www.w3.org/2005/08/addressing"><s:Header><a:Action s:mustUnderstand="1">http://wcf.dian.colombia/IWcfDianCustomerServices/{operation}Response</a:Action></s:Header><s:Body>{body}</s:Body></s:Envelope>
        """;

    private static string GoldenPath(string type, string caseName, string file) => Path.Combine(GoldenRoot(type), caseName, file);

    private static string GoldenRoot(string type)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "golden", type);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("tests/golden not found above " + AppContext.BaseDirectory);
    }
}
