using System.Text;
using System.Text.Json;
using ImpiFact.Dian.Ubl;

namespace ImpiFact.Golden.Tests;

/// <summary>
/// Support document (type 05) and its adjustment note (type 95), phase 7a T4: converting each input.json with the fixed
/// capture context, ImpiFact must produce the same bytes as expected.xml and the same CUDS as expected.cufe, compared
/// exactly like <see cref="InvoiceGoldenTests"/>.
/// </summary>
public sealed class SupportDocumentGoldenTests
{
    /// <summary>Folders of tests/golden covered here.</summary>
    internal static readonly string[] Types = ["support-document", "sd-credit-note"];

    private static readonly ApiDianCatalog Catalog = ApiDianCatalog.Load();

    private static readonly UTF8Encoding Utf8 = new(false);

    public static IEnumerable<TestCaseData> SupportDocuments() => Cases("support-document");

    public static IEnumerable<TestCaseData> AdjustmentNotes() => Cases("sd-credit-note");

    [TestCaseSource(nameof(SupportDocuments))]
    public void Support_document_builder_matches_ApiDian_xml_and_cuds(string caseDir)
    {
        var result = InvoiceUblBuilder.Build(new ApiDianSupportDocumentAdapter(Catalog).MapSupportDocument(Input(caseDir)));

        Assert.That(result.Cufe, Is.EqualTo(ExpectedCode(caseDir)), "CUDS");
        AssertSameBytes(caseDir, result.Xml);
    }

    /// <summary>
    /// php-rules 14, item 7: the PHP writes the invoice software id in sts:SoftwareID of the adjustment note (its
    /// SoftwareSecurityCode and CUDS already use the support-document software). ImpiFact writes the support-document id;
    /// the comparison puts the PHP's id back in that one element, and everything else must match byte for byte.
    /// </summary>
    [TestCaseSource(nameof(AdjustmentNotes))]
    public void Adjustment_note_builder_matches_ApiDian_xml_and_cuds(string caseDir)
    {
        var result = NoteUblBuilder.Build(new ApiDianSupportDocumentAdapter(Catalog).MapAdjustmentNote(Input(caseDir)));

        Assert.That(result.Cude, Is.EqualTo(ExpectedCode(caseDir)), "CUDS");
        var written = SoftwareIdElement(GoldenCaptureContext.SupportDocumentSoftwareId);
        Assert.That(result.Xml, Does.Contain(written), "ImpiFact writes the support-document software id");
        AssertSameBytes(caseDir, result.Xml.Replace(written, SoftwareIdElement(GoldenCaptureContext.SoftwareId), StringComparison.Ordinal));
    }

    // php-rules 13.3, item 2: the PHP wrote a CUFE when the type 11 resolution had a technical key.
    [Test]
    public void A_technical_key_on_the_resolution_does_not_change_the_cuds()
    {
        var caseDir = GoldenCases.OfType("support-document")[0];
        var document = new ApiDianSupportDocumentAdapter(Catalog).MapSupportDocument(Input(caseDir));

        var result = InvoiceUblBuilder.Build(document with { Resolution = document.Resolution with { TechnicalKey = GoldenCaptureContext.TechnicalKey } });

        Assert.That(result.Cufe, Is.EqualTo(ExpectedCode(caseDir)));
        Assert.That(result.Xml, Does.Contain("schemeName=\"CUDS-SHA384\""));
    }

    private static IEnumerable<TestCaseData> Cases(string type) =>
        GoldenCases.OfType(type).Select(dir => new TestCaseData(dir).SetArgDisplayNames(type + "/" + Path.GetFileName(dir)));

    private static JsonElement Input(string caseDir)
    {
        using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "input.json"), Encoding.UTF8));
        return input.RootElement.Clone();
    }

    private static string ExpectedCode(string caseDir) => File.ReadAllText(Path.Combine(caseDir, "expected.cufe")).Trim();

    private static string SoftwareIdElement(string softwareId) =>
        $"CO, DIAN (Dirección de Impuestos y Aduanas Nacionales)\">{softwareId}</sts:SoftwareID>";

    private static void AssertSameBytes(string caseDir, string xml)
    {
        var expected = InvoiceGoldenTests.ExpectedBeforeSigning(caseDir);
        if (!expected.AsSpan().SequenceEqual(Utf8.GetBytes(xml)))
        {
            Assert.Fail(InvoiceGoldenTests.DescribeFirstDifference(Encoding.UTF8.GetString(expected), xml));
        }
    }
}
