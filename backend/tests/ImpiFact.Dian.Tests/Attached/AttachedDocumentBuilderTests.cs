using System.Text;
using System.Xml;
using ImpiFact.Dian.Attached;
using static ImpiFact.Dian.Tests.Attached.AttachedDocumentSamples;

namespace ImpiFact.Dian.Tests.Attached;

// Pruebas del AttachedDocument (contenedor electrónico, tipo de documento 89 de la DIAN): el XML UBL 2.1
// que el emisor entrega al comprador con el documento firmado y el ApplicationResponse (la respuesta de
// validación de la DIAN) dentro de secciones CDATA (texto que el lector XML no interpreta). La referencia
// es la plantilla 89.blade.php de ApiDian, la implementación original en PHP.
public class AttachedDocumentBuilderTests
{
    private const string CbcNs = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private const string CacNs = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";

    private const string SignedDescriptionPath = "/*/cac:Attachment/cac:ExternalReference/cbc:Description";
    private const string ReferencePath = "/*/cac:ParentDocumentLineReference/cac:DocumentReference";
    private const string ApplicationResponseDescriptionPath = ReferencePath + "/cac:Attachment/cac:ExternalReference/cbc:Description";

    [Test]
    public void Root_declares_the_template_namespaces_in_order()
    {
        var xml = AttachedDocumentBuilder.Build(Invoice());

        Assert.That(xml, Does.StartWith(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n" +
            "<AttachedDocument xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:AttachedDocument-2\"" +
            " xmlns:cac=\"urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2\"" +
            " xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\"" +
            " xmlns:ccts=\"urn:un:unece:uncefact:data:specification:CoreComponentTypeSchemaModule:2\"" +
            " xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\"" +
            " xmlns:ext=\"urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2\"" +
            " xmlns:xades=\"http://uri.etsi.org/01903/v1.3.2#\"" +
            " xmlns:xades141=\"http://uri.etsi.org/01903/v1.4.1#\">\n" +
            "  <ext:UBLExtensions>\n" +
            "    <ext:UBLExtension>\n" +
            "      <ext:ExtensionContent/>\n" +
            "    </ext:UBLExtension>\n" +
            "  </ext:UBLExtensions>\n"));
        Assert.That(xml, Does.EndWith("</AttachedDocument>\n"));
    }

    [Test]
    public void Elements_follow_the_template_order()
    {
        var doc = Load(AttachedDocumentBuilder.Build(Invoice()));

        var names = doc.DocumentElement!.SelectNodes("descendant::*")!.Cast<XmlElement>().Select(e => e.Name).ToList();

        string[] party =
        [
            "cac:PartyTaxScheme", "cbc:RegistrationName", "cbc:CompanyID", "cbc:TaxLevelCode",
            "cac:TaxScheme", "cbc:ID", "cbc:Name",
        ];
        string[] attachment = ["cac:Attachment", "cac:ExternalReference", "cbc:MimeCode", "cbc:EncodingCode", "cbc:Description"];
        string[] expected =
        [
            "ext:UBLExtensions", "ext:UBLExtension", "ext:ExtensionContent",
            "cbc:UBLVersionID", "cbc:CustomizationID", "cbc:ProfileID", "cbc:ProfileExecutionID", "cbc:ID",
            "cbc:IssueDate", "cbc:IssueTime", "cbc:DocumentType", "cbc:ParentDocumentID",
            "cac:SenderParty", .. party,
            "cac:ReceiverParty", .. party,
            .. attachment,
            "cac:ParentDocumentLineReference", "cbc:LineID",
            "cac:DocumentReference", "cbc:ID", "cbc:UUID", "cbc:IssueDate", "cbc:DocumentType",
            .. attachment,
            "cac:ResultOfVerification", "cbc:ValidatorID", "cbc:ValidationResultCode", "cbc:ValidationDate", "cbc:ValidationTime",
        ];
        Assert.That(names, Is.EqualTo(expected));
    }

    [Test]
    public void Header_carries_the_dian_literals_the_key_and_the_document_number()
    {
        var doc = Load(AttachedDocumentBuilder.Build(Invoice()));

        Assert.That(Text(doc, "/*/cbc:UBLVersionID"), Is.EqualTo("UBL 2.1"));
        Assert.That(Text(doc, "/*/cbc:CustomizationID"), Is.EqualTo("Documentos adjuntos"));
        Assert.That(Text(doc, "/*/cbc:ProfileID"), Is.EqualTo("Factura Electrónica de Venta"));
        Assert.That(Text(doc, "/*/cbc:ProfileExecutionID"), Is.EqualTo("2"));
        Assert.That(Text(doc, "/*/cbc:ID"), Is.EqualTo(InvoiceCufe));
        Assert.That(Text(doc, "/*/cbc:DocumentType"), Is.EqualTo("Contenedor de Factura Electrónica"));
        Assert.That(Text(doc, "/*/cbc:ParentDocumentID"), Is.EqualTo("SETP990000002"));
        Assert.That(Text(doc, ReferencePath + "/cbc:ID"), Is.EqualTo("SETP990000002"));
        Assert.That(Text(doc, ReferencePath + "/cbc:UUID"), Is.EqualTo(InvoiceCufe));
        Assert.That(Text(doc, ReferencePath + "/cbc:DocumentType"), Is.EqualTo("ApplicationResponse"));
        Assert.That(Text(doc, "/*/cac:ParentDocumentLineReference/cbc:LineID"), Is.EqualTo("1"));
    }

    [Test]
    public void Production_environment_is_written_as_profile_execution_id_1()
    {
        var doc = Load(AttachedDocumentBuilder.Build(Invoice() with { Environment = ImpiFact.Dian.Cufe.DianEnvironment.Production }));

        Assert.That(Text(doc, "/*/cbc:ProfileExecutionID"), Is.EqualTo("1"));
    }

    // Como el PHP (Carbon::now() en America/Bogota): la fecha y la hora de generación del contenedor van en
    // hora de Colombia, y la fecha también va en cac:DocumentReference/cbc:IssueDate.
    [Test]
    public void Generation_time_is_written_in_colombian_time_whatever_the_input_offset()
    {
        var madrid = GeneratedAt.ToOffset(TimeSpan.FromHours(1));
        var doc = Load(AttachedDocumentBuilder.Build(Invoice() with { GeneratedAt = madrid }));

        Assert.That(Text(doc, "/*/cbc:IssueDate"), Is.EqualTo("2026-11-16"));
        Assert.That(Text(doc, "/*/cbc:IssueTime"), Is.EqualTo("09:15:31-05:00"));
        Assert.That(Text(doc, ReferencePath + "/cbc:IssueDate"), Is.EqualTo("2026-11-16"));
    }

    [Test]
    public void Parties_carry_the_tax_data_and_only_the_receiver_carries_the_organization_type()
    {
        var doc = Load(AttachedDocumentBuilder.Build(Invoice()));

        var sender = (XmlElement)doc.SelectSingleNode("/*/cac:SenderParty/cac:PartyTaxScheme", Ns(doc))!;
        Assert.That(Text(sender, "cbc:RegistrationName"), Is.EqualTo("EMPRESA DE PRUEBA SAS"));
        Assert.That(Attributes(sender, "cbc:CompanyID"), Is.EqualTo(new[]
        {
            ("schemeAgencyID", "195"), ("schemeAgencyName", "CO, DIAN (Dirección de Impuestos y Aduanas Nacionales)"),
            ("schemeID", "2"), ("schemeName", "31"),
        }));
        Assert.That(Text(sender, "cbc:CompanyID"), Is.EqualTo("900000001"));
        Assert.That(Text(sender, "cbc:TaxLevelCode"), Is.EqualTo("O-23"));
        Assert.That(Text(sender, "cbc:TaxLevelCode/@listName"), Is.EqualTo("48"));
        Assert.That(Text(sender, "cac:TaxScheme/cbc:ID"), Is.EqualTo("01"));
        Assert.That(Text(sender, "cac:TaxScheme/cbc:Name"), Is.EqualTo("IVA"));

        // Sin dígito de verificación, el PHP escribe schemeID vacío.
        var receiver = (XmlElement)doc.SelectSingleNode("/*/cac:ReceiverParty/cac:PartyTaxScheme", Ns(doc))!;
        Assert.That(Text(receiver, "cbc:RegistrationName"), Is.EqualTo("PERSONA & HIJOS <PRUEBA>"));
        Assert.That(Attributes(receiver, "cbc:CompanyID"), Is.EqualTo(new[]
        {
            ("schemeAgencyID", "195"), ("schemeAgencyName", "CO, DIAN (Dirección de Impuestos y Aduanas Nacionales)"),
            ("schemeID", ""), ("schemeName", "13"), ("schemeVersionID", "2"),
        }));
        Assert.That(Text(receiver, "cbc:CompanyID"), Is.EqualTo("1020304050"));
        Assert.That(Text(receiver, "cbc:TaxLevelCode"), Is.EqualTo("R-99-PN"));
        Assert.That(Text(receiver, "cbc:TaxLevelCode/@listName"), Is.EqualTo("49"));
        Assert.That(Text(receiver, "cac:TaxScheme/cbc:ID"), Is.EqualTo("ZZ"));
        Assert.That(Text(receiver, "cac:TaxScheme/cbc:Name"), Is.EqualTo("No aplica"));
    }

    [Test]
    public void Attachments_are_text_xml_in_utf8()
    {
        var doc = Load(AttachedDocumentBuilder.Build(Invoice()));

        foreach (var path in new[] { "/*/cac:Attachment/cac:ExternalReference", ReferencePath + "/cac:Attachment/cac:ExternalReference" })
        {
            Assert.That(Text(doc, path + "/cbc:MimeCode"), Is.EqualTo("text/xml"));
            Assert.That(Text(doc, path + "/cbc:EncodingCode"), Is.EqualTo("UTF-8"));
        }
    }

    [Test]
    public void Embedded_documents_are_cdata_sections_that_round_trip_exactly()
    {
        var input = Invoice();
        var xml = AttachedDocumentBuilder.Build(input);
        var doc = Load(xml);

        Assert.That(xml, Does.Contain("<cbc:Description><![CDATA[" + SignedInvoice + "]]></cbc:Description>"));
        Assert.That(xml, Does.Contain("<cbc:Description><![CDATA[" + ApplicationResponse + "]]></cbc:Description>"));
        Assert.That(CdataText(doc, SignedDescriptionPath), Is.EqualTo(SignedInvoice));
        Assert.That(Encoding.UTF8.GetBytes(CdataText(doc, ApplicationResponseDescriptionPath)), Is.EqualTo(input.ApplicationResponseXml));
    }

    // Como el PHP: ValidationDate y ValidationTime son el texto del primer IssueDate y del primer IssueTime
    // del ApplicationResponse (el de su raíz), sin cambios.
    [Test]
    public void Validation_date_and_time_come_from_the_first_issue_date_and_time_of_the_application_response()
    {
        var doc = Load(AttachedDocumentBuilder.Build(Invoice()));

        Assert.That(Text(doc, ReferencePath + "/cac:ResultOfVerification/cbc:ValidationDate"), Is.EqualTo("2026-11-16"));
        Assert.That(Text(doc, ReferencePath + "/cac:ResultOfVerification/cbc:ValidationTime"), Is.EqualTo("09:15:27-05:00"));
    }

    [Test]
    public void Result_of_verification_is_code_02_from_the_dian()
    {
        var doc = Load(AttachedDocumentBuilder.Build(Invoice()));

        Assert.That(Text(doc, ReferencePath + "/cac:ResultOfVerification/cbc:ValidatorID"),
            Is.EqualTo("Unidad Especial Dirección de Impuestos Y Aduanas Nacionales"));
        Assert.That(Text(doc, ReferencePath + "/cac:ResultOfVerification/cbc:ValidationResultCode"), Is.EqualTo("02"));
    }

    // La factura se identifica con su CUFE (código único de factura electrónica) y las notas con su CUDE
    // (código único de documento electrónico): el anexo técnico (AE43) pide CUFE-SHA384 o CUDE-SHA384. Es la
    // única diferencia entre los tres tipos; el PHP escribe CUFE-SHA384 siempre.
    [TestCase(AttachedDocumentKind.Invoice, "CUFE-SHA384")]
    [TestCase(AttachedDocumentKind.CreditNote, "CUDE-SHA384")]
    [TestCase(AttachedDocumentKind.DebitNote, "CUDE-SHA384")]
    public void Document_key_scheme_depends_on_the_kind(AttachedDocumentKind kind, string scheme)
    {
        var doc = Load(AttachedDocumentBuilder.Build(Invoice() with { Kind = kind }));

        Assert.That(Text(doc, ReferencePath + "/cbc:UUID/@schemeName"), Is.EqualTo(scheme));
    }

    [TestCase(AttachedDocumentKind.CreditNote)]
    [TestCase(AttachedDocumentKind.DebitNote)]
    public void Notes_differ_from_the_invoice_only_in_the_key_scheme(AttachedDocumentKind kind)
    {
        var invoice = AttachedDocumentBuilder.Build(Invoice());
        var note = AttachedDocumentBuilder.Build(Invoice() with { Kind = kind });

        Assert.That(note, Is.EqualTo(invoice.Replace("schemeName=\"CUFE-SHA384\"", "schemeName=\"CUDE-SHA384\"", StringComparison.Ordinal)));
    }

    // El texto de una sección CDATA no puede contener "]]>" (es su marca de cierre). Un XML embebido lo
    // contiene cuando él mismo tiene secciones CDATA. Como libxml (la biblioteca XML del PHP), la sección
    // se parte: cada "]]>" queda como "]]" al final de una sección y ">" al inicio de la siguiente.
    [Test]
    public void Cdata_terminator_inside_an_embedded_document_splits_the_section()
    {
        const string signed = "<a><![CDATA[x]]></a>";
        const string response = "<r><cbc:IssueDate xmlns:cbc=\"" + CbcNs + "\">2026-11-16</cbc:IssueDate>" +
                                "<cbc:IssueTime xmlns:cbc=\"" + CbcNs + "\">09:15:27-05:00</cbc:IssueTime><![CDATA[]]]]></r>";
        var input = Invoice() with { SignedDocumentXml = signed, ApplicationResponseXml = Encoding.UTF8.GetBytes(response) };

        var xml = AttachedDocumentBuilder.Build(input);
        var doc = Load(xml);

        Assert.That(xml, Does.Contain("<cbc:Description><![CDATA[<a><![CDATA[x]]]]><![CDATA[></a>]]></cbc:Description>"));
        Assert.That(xml, Does.Contain("<![CDATA[]]]]]]><![CDATA[></r>]]></cbc:Description>"));
        Assert.That(CdataText(doc, SignedDescriptionPath), Is.EqualTo(signed));
        Assert.That(CdataText(doc, ApplicationResponseDescriptionPath), Is.EqualTo(response));
    }

    // Lo embebido se copia tal cual, aunque contenga el texto de una etiqueta del propio contenedor.
    [Test]
    public void Embedded_documents_that_look_like_container_markup_are_copied_verbatim()
    {
        const string signed = "<a><b>&lt;cbc:Description&gt;</b><!-- <cbc:Description>x</cbc:Description> --></a>";
        var input = Invoice() with { SignedDocumentXml = signed };

        var doc = Load(AttachedDocumentBuilder.Build(input));

        Assert.That(CdataText(doc, SignedDescriptionPath), Is.EqualTo(signed));
        Assert.That(CdataText(doc, ApplicationResponseDescriptionPath), Is.EqualTo(ApplicationResponse));
    }

    // Una marca de orden de bytes (BOM) al inicio del ApplicationResponse se conserva dentro del CDATA,
    // como en el PHP, que copia los bytes tal cual.
    [Test]
    public void Byte_order_mark_of_the_application_response_is_kept()
    {
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(ApplicationResponse)).ToArray();

        var doc = Load(AttachedDocumentBuilder.Build(Invoice() with { ApplicationResponseXml = withBom }));

        Assert.That(Encoding.UTF8.GetBytes(CdataText(doc, ApplicationResponseDescriptionPath)), Is.EqualTo(withBom));
        Assert.That(Text(doc, ReferencePath + "/cac:ResultOfVerification/cbc:ValidationTime"), Is.EqualTo("09:15:27-05:00"));
    }

    [Test]
    public void Rejects_an_application_response_without_issue_date_or_time()
    {
        var noTime = "<r><cbc:IssueDate xmlns:cbc=\"" + CbcNs + "\">2026-11-16</cbc:IssueDate></r>";

        var error = Assert.Throws<ArgumentException>(() =>
            AttachedDocumentBuilder.Build(Invoice() with { ApplicationResponseXml = Encoding.UTF8.GetBytes(noTime) }));
        Assert.That(error!.Message, Does.Contain("IssueTime"));
    }

    [Test]
    public void Rejects_an_application_response_that_is_not_xml()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            AttachedDocumentBuilder.Build(Invoice() with { ApplicationResponseXml = Encoding.UTF8.GetBytes("<html>") }));
        Assert.That(error!.ParamName, Is.EqualTo("input"));
    }

    [Test]
    public void Rejects_an_application_response_that_is_not_utf8()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            AttachedDocumentBuilder.Build(Invoice() with { ApplicationResponseXml = [0x3C, 0xFF, 0x3E] }));
        Assert.That(error!.Message, Does.Contain("UTF-8"));
    }

    [Test]
    public void Rejects_an_empty_signed_document()
    {
        Assert.Throws<ArgumentException>(() => AttachedDocumentBuilder.Build(Invoice() with { SignedDocumentXml = "" }));
    }

    private static XmlDocument Load(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        doc.Load(reader);
        return doc;
    }

    private static XmlNamespaceManager Ns(XmlNode node)
    {
        var doc = node as XmlDocument ?? node.OwnerDocument!;
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("cbc", CbcNs);
        ns.AddNamespace("cac", CacNs);
        return ns;
    }

    private static string? Text(XmlNode context, string xpath)
    {
        var node = context.SelectSingleNode(xpath, Ns(context));
        return node is XmlAttribute attribute ? attribute.Value : node?.InnerText;
    }

    private static (string, string)[] Attributes(XmlNode context, string xpath) =>
        ((XmlElement)context.SelectSingleNode(xpath, Ns(context))!).Attributes.Cast<XmlAttribute>()
            .Select(a => (a.Name, a.Value)).ToArray();

    // El texto de cbc:Description debe ser solo secciones CDATA; se unen como lo hace un lector XML.
    private static string CdataText(XmlDocument doc, string xpath)
    {
        var description = doc.SelectSingleNode(xpath, Ns(doc))!;
        Assert.That(description.ChildNodes.Cast<XmlNode>().Select(n => n.NodeType), Is.All.EqualTo(XmlNodeType.CDATA));
        return description.InnerText;
    }
}
