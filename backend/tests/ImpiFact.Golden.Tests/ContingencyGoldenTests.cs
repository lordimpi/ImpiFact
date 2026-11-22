using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ImpiFact.Dian.Ubl;

namespace ImpiFact.Golden.Tests;

/// <summary>
/// Contingency invoices (phase 7a, T5; php-rules, section 12): type 03, the transcription of a paper invoice issued while
/// the invoicer could not issue electronically, and type 04, the invoice issued while the DIAN could not validate. Each
/// input.json is converted with the fixed capture context and compared with expected.xml and expected.cufe exactly like
/// <see cref="InvoiceGoldenTests"/>, except for the documented deviations of type 04.
/// </summary>
public sealed partial class ContingencyGoldenTests
{
    /// <summary>Folders of tests/golden covered here.</summary>
    internal static readonly string[] Types = ["invoice-contingency", "invoice-contingency-4"];

    private static readonly ApiDianCatalog Catalog = ApiDianCatalog.Load();

    private static readonly UTF8Encoding Utf8 = new(false);

    public static IEnumerable<TestCaseData> PaperBackupCases() => Cases("invoice-contingency");

    public static IEnumerable<TestCaseData> DianContingencyCases() => Cases("invoice-contingency-4");

    /// <summary>
    /// Type 03: annex 1.9, section 11.1.4.1 and its Nota-2: the transcription of a contingency invoice computes the CUDE with
    /// the software PIN (that numbering has no technical key) and the XML references the paper invoice. Byte for byte.
    /// </summary>
    [TestCaseSource(nameof(PaperBackupCases))]
    public void Type_03_builder_matches_ApiDian_xml_and_cude(string caseDir)
    {
        var result = InvoiceUblBuilder.Build(new ApiDianInvoiceAdapter(Catalog).MapContingency(Input(caseDir)));

        Assert.That(result.Cufe, Is.EqualTo(ExpectedCode(caseDir)), "CUDE");
        Assert.That(result.Xml, Does.Contain("schemeName=\"CUDE-SHA384\""));
        AssertSameBytes(caseDir, result.Xml);
    }

    // The Postman resolution of type 3 carries a technical key; the CUDE of the transcription never uses it.
    [Test]
    public void A_technical_key_on_the_type_03_resolution_does_not_change_the_cude()
    {
        var caseDir = GoldenCases.OfType("invoice-contingency")[0];
        var invoice = new ApiDianInvoiceAdapter(Catalog).MapContingency(Input(caseDir));

        var result = InvoiceUblBuilder.Build(invoice with { Resolution = invoice.Resolution with { TechnicalKey = "" } });

        Assert.That(result.Cufe, Is.EqualTo(ExpectedCode(caseDir)));
    }

    /// <summary>
    /// Type 04 (php-rules 12.2, item 3, and the case.json of the golden): annex 1.9, section 12.2, says the type 04 invoice
    /// keeps the numbering of the electronic sales invoice («La numeración utilizada para este tipo de documento es la de
    /// Factura electrónica de venta»), so ImpiFact writes it as the invoice does: the CUFE with the technical key of that
    /// resolution (rules FAD06 and FAD08: <c>CUFE-SHA384</c>) and its sts:InvoiceControl (rule FAB05a). The PHP wrote
    /// <c>CUFE-SHA384</c> with a value computed with the software PIN and no sts:InvoiceControl. The comparison swaps
    /// back those two documented elements, the code (in cbc:UUID and sts:QRCode) and sts:InvoiceControl, and everything
    /// else must match byte for byte.
    /// </summary>
    [TestCaseSource(nameof(DianContingencyCases))]
    public void Type_04_builder_writes_the_annex_cufe_and_otherwise_matches_ApiDian(string caseDir)
    {
        var invoice = new ApiDianInvoiceAdapter(Catalog).Map(Input(caseDir));

        var result = InvoiceUblBuilder.Build(invoice);

        var phpCode = ExpectedCode(caseDir);
        var root = Load(result.Xml);
        Assert.Multiple(() =>
        {
            Assert.That(result.Cufe, Is.EqualTo(GoldenCodeTests.Recompute(root, "CUFE", GoldenCaptureContext.TechnicalKey)),
                "ImpiFact computes the CUFE of its own XML with the technical key, as the annex asks.");
            Assert.That(result.Cufe, Is.Not.EqualTo(phpCode));
            Assert.That(GoldenCodeTests.Recompute(root, "CUFE", GoldenCaptureContext.SoftwarePin), Is.EqualTo(phpCode),
                "The PHP value is the same formula with the software PIN in place of the technical key.");
            Assert.That(result.Xml, Does.Contain("schemeName=\"CUFE-SHA384\""));
            Assert.That(result.Xml, Does.Contain("<sts:InvoiceAuthorization>18760000001</sts:InvoiceAuthorization>"));
        });
        var asPhp = InvoiceControl().Replace(result.Xml.Replace(result.Cufe, phpCode, StringComparison.Ordinal), "");
        AssertSameBytes(caseDir, asPhp);
    }

    private static IEnumerable<TestCaseData> Cases(string type) =>
        GoldenCases.OfType(type).Select(dir => new TestCaseData(dir).SetArgDisplayNames(type + "/" + Path.GetFileName(dir)));

    private static JsonElement Input(string caseDir)
    {
        using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "input.json"), Encoding.UTF8));
        return input.RootElement.Clone();
    }

    private static string ExpectedCode(string caseDir) => File.ReadAllText(Path.Combine(caseDir, "expected.cufe")).Trim();

    private static XElement Load(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader).Root!;
    }

    private static void AssertSameBytes(string caseDir, string xml)
    {
        var expected = InvoiceGoldenTests.ExpectedBeforeSigning(caseDir);
        if (!expected.AsSpan().SequenceEqual(Utf8.GetBytes(xml)))
        {
            Assert.Fail(InvoiceGoldenTests.DescribeFirstDifference(Encoding.UTF8.GetString(expected), xml));
        }
    }

    /// <summary>The sts:InvoiceControl element with the line break and indentation before it.</summary>
    [GeneratedRegex(@"\n *<sts:InvoiceControl>.*?</sts:InvoiceControl>", RegexOptions.Singleline)]
    private static partial Regex InvoiceControl();
}
