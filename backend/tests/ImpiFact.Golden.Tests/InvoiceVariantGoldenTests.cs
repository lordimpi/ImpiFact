using System.Text;
using System.Text.Json;
using ImpiFact.Dian.Ubl;

namespace ImpiFact.Golden.Tests;

/// <summary>
/// AIU, mandate and freight transport invoices (phase 7a, T2): template 01 with per-variant options. Converting each
/// input.json with the fixed capture context, ImpiFact must produce the same bytes as expected.xml and the same CUFE as
/// expected.cufe, compared exactly like <see cref="InvoiceGoldenTests"/>.
/// </summary>
public sealed class InvoiceVariantGoldenTests
{
    /// <summary>Folders of tests/golden covered here, with the variant each one is.</summary>
    internal static readonly (string Type, ApiDianInvoiceVariant Variant)[] Types =
    [
        ("invoice-aiu", ApiDianInvoiceVariant.Aiu),
        ("invoice-mandate", ApiDianInvoiceVariant.Mandate),
        ("invoice-transport", ApiDianInvoiceVariant.Transport),
    ];

    private static readonly ApiDianCatalog Catalog = ApiDianCatalog.Load();

    public static IEnumerable<TestCaseData> Cases() =>
        Types.SelectMany(type => GoldenCases.OfType(type.Type).Select(dir =>
            new TestCaseData(dir, type.Variant).SetArgDisplayNames(type.Type + "/" + Path.GetFileName(dir))));

    [TestCaseSource(nameof(Cases))]
    public void Builder_matches_ApiDian_xml_and_cufe(string caseDir, ApiDianInvoiceVariant variant)
    {
        var result = InvoiceUblBuilder.Build(Map(caseDir, variant));

        Assert.That(result.Cufe, Is.EqualTo(File.ReadAllText(Path.Combine(caseDir, "expected.cufe")).Trim()), "CUFE");
        var expected = InvoiceGoldenTests.ExpectedBeforeSigning(caseDir);
        if (!expected.AsSpan().SequenceEqual(new UTF8Encoding(false).GetBytes(result.Xml)))
        {
            Assert.Fail(InvoiceGoldenTests.DescribeFirstDifference(Encoding.UTF8.GetString(expected), result.Xml));
        }
    }

    // php-rules 10.1, item 3: the AIU note goes only on lines whose description is exactly «Administración».
    [Test]
    public void The_aiu_note_goes_only_on_the_administration_lines()
    {
        var invoice = Map(GoldenCases.OfType("invoice-aiu")[0], ApiDianInvoiceVariant.Aiu);
        invoice = invoice with
        {
            Lines = [invoice.Lines[0] with { Description = "Administracion" }, invoice.Lines[0], invoice.Lines[0] with { Description = "administración" }],
        };

        var xml = InvoiceUblBuilder.Build(invoice).Xml;

        Assert.That(xml.Split("Contrato de servicios AIU por concepto de: OBRA DE PRUEBA GOLDEN").Length - 1, Is.EqualTo(1));
        Assert.That(xml, Does.Contain("<cbc:ID>2</cbc:ID>\n    <cbc:Note>Contrato de servicios AIU"));
    }

    // A standard invoice whose lines say «Administración» gets no AIU note: only the AIU variant writes it.
    [Test]
    public void A_standard_invoice_writes_no_aiu_note_nor_line_scheme()
    {
        using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenCases.OfType("invoice-aiu")[0], "input.json"), Encoding.UTF8));

        var xml = InvoiceUblBuilder.Build(new ApiDianInvoiceAdapter(Catalog).Map(input.RootElement)).Xml;

        Assert.That(xml, Does.Not.Contain("Contrato de servicios AIU"));
        Assert.That(xml, Does.Not.Contain("schemeID=\"1\">1</cbc:ID>"));
        Assert.That(xml, Does.Contain("<cbc:CustomizationID>10</cbc:CustomizationID>"));
    }

    private static SalesInvoice Map(string caseDir, ApiDianInvoiceVariant variant)
    {
        using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "input.json"), Encoding.UTF8));
        return new ApiDianInvoiceAdapter(Catalog).MapVariant(input.RootElement, variant);
    }
}
