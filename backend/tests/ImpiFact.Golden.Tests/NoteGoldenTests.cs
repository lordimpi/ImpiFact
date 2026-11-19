using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Xml;
using ImpiFact.Dian.Signing;
using ImpiFact.Dian.Ubl;

namespace ImpiFact.Golden.Tests;

/// <summary>
/// Casos de referencia de notas crédito y débito de ApiDian (el PHP original): al convertir input.json con
/// la configuración fija de captura, ImpiFact debe producir los mismos bytes que expected.xml y el mismo
/// CUDE (código único de documento electrónico) que expected.cufe. El único ajuste al comparar es el del
/// lugar vacío de la firma, igual que en <see cref="InvoiceGoldenTests"/>.
/// </summary>
public sealed class NoteGoldenTests
{
    private const string DsNs = "http://www.w3.org/2000/09/xmldsig#";
    private const string ExtNs = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";
    private const string SignaturePath = "/*/ext:UBLExtensions/ext:UBLExtension[2]/ext:ExtensionContent/ds:Signature";

    private static readonly ApiDianCatalog Catalog = ApiDianCatalog.Load();

    public static IEnumerable<TestCaseData> Cases() =>
        GoldenCases.CreditNotes().Select(dir => new TestCaseData(dir).SetArgDisplayNames("credit-note/" + Path.GetFileName(dir)))
            .Concat(GoldenCases.DebitNotes().Select(dir => new TestCaseData(dir).SetArgDisplayNames("debit-note/" + Path.GetFileName(dir))));

    [TestCaseSource(nameof(Cases))]
    public void Builder_matches_ApiDian_xml_and_cude(string caseDir)
    {
        var result = NoteUblBuilder.Build(Map(caseDir));

        var expectedCude = File.ReadAllText(Path.Combine(caseDir, "expected.cufe")).Trim();
        Assert.That(result.Cude, Is.EqualTo(expectedCude), "CUDE");

        var expected = InvoiceGoldenTests.ExpectedBeforeSigning(caseDir);
        var actual = new UTF8Encoding(false).GetBytes(result.Xml);
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            Assert.Fail(InvoiceGoldenTests.DescribeFirstDifference(Encoding.UTF8.GetString(expected), result.Xml));
        }
    }

    // XmlSigner ubica el lugar de la firma por posición (el segundo ext:ExtensionContent), así que firma una
    // nota igual que una factura.
    [TestCaseSource(nameof(Cases))]
    public void Signed_builder_output_passes_SignedXml_verification(string caseDir)
    {
        var result = NoteUblBuilder.Build(Map(caseDir));
        using var certificate = CreateThrowawayCertificate();

        var signed = XmlSigner.Sign(result.Xml, certificate, new DateTimeOffset(2026, 11, 14, 16, 0, 0, TimeSpan.Zero));

        var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using (var reader = XmlReader.Create(new StringReader(signed), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
        {
            doc.Load(reader);
        }

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("ds", DsNs);
        ns.AddNamespace("ext", ExtNs);
        var signature = doc.SelectSingleNode(SignaturePath, ns) as XmlElement;
        Assert.That(signature, Is.Not.Null, "ds:Signature in the second ext:ExtensionContent");
        var checker = new SignedXml(doc);
        checker.LoadXml(signature!);
        Assert.That(checker.CheckSignature(certificate, verifySignatureOnly: true), Is.True);
    }

    // La plantilla de ApiDian escribe cbc:Note justo después del tipo de documento (o de la hora, en la
    // nota débito, que no lleva tipo) cuando la solicitud trae «notes».
    [TestCase("credit-note", "<cbc:CreditNoteTypeCode>91</cbc:CreditNoteTypeCode>\n  <cbc:Note>Nota de prueba</cbc:Note>\n  <cbc:DocumentCurrencyCode>")]
    [TestCase("debit-note", "<cbc:IssueTime>11:10:00-05:00</cbc:IssueTime>\n  <cbc:Note>Nota de prueba</cbc:Note>\n  <cbc:DocumentCurrencyCode>")]
    public void Note_text_follows_the_type_code_or_the_issue_time(string documentType, string expectedFragment)
    {
        var caseDir = documentType == "credit-note" ? GoldenCases.CreditNotes()[0] : GoldenCases.DebitNotes()[0];
        var note = Map(caseDir) with { Note = "Nota de prueba" };

        var result = NoteUblBuilder.Build(note);

        Assert.That(result.Xml, Does.Contain(expectedFragment));
    }

    // Una línea gratuita de una nota lleva su precio de referencia y su tipo (cac:PricingReference) y escribe
    // 0.00 como precio (plantillas _credit_note_lines y _debit_note_lines de ApiDian).
    [TestCase("credit-note", "cac:CreditNoteLine")]
    [TestCase("debit-note", "cac:DebitNoteLine")]
    public void Free_of_charge_line_writes_the_reference_price_and_a_zero_price(string documentType, string lineElement)
    {
        var caseDir = documentType == "credit-note" ? GoldenCases.CreditNotes()[0] : GoldenCases.DebitNotes()[0];
        var note = Map(caseDir);
        var freeLine = note.Lines[0] with { IsFreeOfCharge = true, ReferencePriceCode = "01" };
        note = note with { Lines = [freeLine] };
        var price = Ubl2(freeLine.PriceAmount);

        var result = NoteUblBuilder.Build(note);

        var line = Element(result.Xml, lineElement);
        Assert.That(line, Does.Contain(
            "<cac:PricingReference>\n" +
            "      <cac:AlternativeConditionPrice>\n" +
            $"        <cbc:PriceAmount currencyID=\"COP\">{price}</cbc:PriceAmount>\n" +
            "        <cbc:PriceTypeCode>01</cbc:PriceTypeCode>\n" +
            "      </cac:AlternativeConditionPrice>\n" +
            "    </cac:PricingReference>"));
        Assert.That(line, Does.Contain("<cac:Price>\n      <cbc:PriceAmount currencyID=\"COP\">0.00</cbc:PriceAmount>"));
    }

    // Las notas del PHP también escriben un cac:PaymentMeans por forma de pago (_payment_means.blade.php), con
    // cbc:PaymentID desde 0, y no llevan cac:PaymentTerms.
    [TestCase("credit-note")]
    [TestCase("debit-note")]
    public void Each_payment_form_of_a_note_is_one_payment_means(string documentType)
    {
        var caseDir = documentType == "credit-note" ? GoldenCases.CreditNotes()[0] : GoldenCases.DebitNotes()[0];
        var note = Map(caseDir);
        note = note with { Payments = [note.Payments[0], new PaymentTerms("2", "48", new DateOnly(2026, 12, 31), 30)] };

        var xml = NoteUblBuilder.Build(note).Xml;

        Assert.That(xml, Does.Contain(
            "    <cbc:PaymentID>0</cbc:PaymentID>\n" +
            "  </cac:PaymentMeans>\n" +
            "  <cac:PaymentMeans>\n" +
            "    <cbc:ID>2</cbc:ID>\n" +
            "    <cbc:PaymentMeansCode>48</cbc:PaymentMeansCode>\n" +
            "    <cbc:PaymentDueDate>2026-12-31</cbc:PaymentDueDate>\n" +
            "    <cbc:PaymentID>1</cbc:PaymentID>\n" +
            "  </cac:PaymentMeans>\n"));
        Assert.That(xml, Does.Not.Contain("cac:PaymentTerms"));
    }

    [Test]
    public void Free_of_charge_line_without_a_reference_price_is_rejected()
    {
        var note = Map(GoldenCases.CreditNotes()[0]);
        note = note with { Lines = [note.Lines[0] with { IsFreeOfCharge = true, ReferencePriceCode = null }] };

        var error = Assert.Throws<ArgumentException>(() => NoteUblBuilder.Build(note));
        Assert.That(error!.Message, Does.Contain("reference price"));
    }

    private static SalesNote Map(string caseDir)
    {
        using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "input.json"), Encoding.UTF8));
        return new ApiDianNoteAdapter(Catalog).Map(input.RootElement);
    }

    private static string Ubl2(decimal value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    private static string Element(string xml, string name)
    {
        var start = xml.IndexOf("<" + name + ">", StringComparison.Ordinal);
        var end = xml.IndexOf("</" + name + ">", start, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), name);
        return xml[start..end];
    }

    private static X509Certificate2 CreateThrowawayCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=ImpiFact Golden Signer, C=CO", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var notBefore = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return request.CreateSelfSigned(notBefore, notBefore.AddYears(1));
    }
}
