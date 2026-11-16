using System.Xml.Linq;
using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Rounding;

namespace ImpiFact.Dian.Tests.Cufe;

public class CufeCalculatorTests
{
    // Fixed values used by tools/golden-capture when the fixtures were produced.
    private const string TechnicalKey = "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c";
    private const string SoftwarePin = "12345";

    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";

    public static IEnumerable<TestCaseData> GoldenCases() =>
        GoldenFixtures.All().Select(c => new TestCaseData(c).SetName($"{c.DocumentType}/{c.Name}"));

    [TestCaseSource(nameof(GoldenCases))]
    public void Matches_the_code_ApiDian_produced(GoldenFixture fixture)
    {
        var xml = XDocument.Parse(fixture.ExpectedXml).Root!;
        var input = ReadInput(xml, fixture.DocumentType);

        var code = fixture.DocumentType == "invoice"
            ? CufeCalculator.ComputeCufe(input, TechnicalKey)
            : CufeCalculator.ComputeCude(input, SoftwarePin);

        Assert.That(code, Is.EqualTo(fixture.ExpectedCode));
    }

    [Test]
    public void Missing_taxes_count_as_zero()
    {
        var withoutTaxes = Sample() with { Iva = null, Inc = null, Ica = null };
        var withZeroTaxes = Sample() with { Iva = 0m, Inc = 0m, Ica = 0m };

        Assert.That(CufeCalculator.ComputeCufe(withoutTaxes, TechnicalKey),
            Is.EqualTo(CufeCalculator.ComputeCufe(withZeroTaxes, TechnicalKey)));
    }

    [Test]
    public void Returns_96_lowercase_hex_characters()
    {
        var code = CufeCalculator.ComputeCufe(Sample(), TechnicalKey);

        Assert.That(code, Does.Match("^[0-9a-f]{96}$"));
    }

    [Test]
    public void Cufe_and_cude_differ_only_by_the_secret()
    {
        Assert.That(CufeCalculator.ComputeCufe(Sample(), "same"), Is.EqualTo(CufeCalculator.ComputeCude(Sample(), "same")));
    }

    private static CufeInput Sample() => new(
        Number: "SETP990000001",
        IssueDate: new DateOnly(2026, 11, 14),
        IssueTime: new TimeOnly(10, 0, 0),
        LineExtensionAmount: 1900m,
        Iva: 361m,
        Inc: null,
        Ica: null,
        PayableAmount: 2161m,
        SupplierNit: "900000001",
        CustomerNit: "800000001",
        Environment: DianEnvironment.Testing);

    private static CufeInput ReadInput(XElement root, string documentType)
    {
        var totals = root.Element(Cac + (documentType == "debit-note" ? "RequestedMonetaryTotal" : "LegalMonetaryTotal"))!;

        decimal? Tax(string schemeId) => root.Elements(Cac + "TaxTotal")
            .Where(t => t.Element(Cac + "TaxSubtotal")!.Element(Cac + "TaxCategory")!
                         .Element(Cac + "TaxScheme")!.Element(Cbc + "ID")!.Value == schemeId)
            .Select(t => (decimal?)DianRounding.Parse(t.Element(Cbc + "TaxAmount")!.Value))
            .FirstOrDefault();

        string PartyNit(string party) => root.Element(Cac + party)!.Element(Cac + "Party")!
            .Element(Cac + "PartyTaxScheme")!.Element(Cbc + "CompanyID")!.Value;

        // IssueTime carries the fixed Colombian offset, e.g. "10:00:00-05:00".
        var time = root.Element(Cbc + "IssueTime")!.Value[..8];

        return new CufeInput(
            Number: root.Element(Cbc + "ID")!.Value,
            IssueDate: DateOnly.Parse(root.Element(Cbc + "IssueDate")!.Value, System.Globalization.CultureInfo.InvariantCulture),
            IssueTime: TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture),
            LineExtensionAmount: DianRounding.Parse(totals.Element(Cbc + "LineExtensionAmount")!.Value),
            Iva: Tax("01"),
            Inc: Tax("04"),
            Ica: Tax("03"),
            PayableAmount: DianRounding.Parse(totals.Element(Cbc + "PayableAmount")!.Value),
            SupplierNit: PartyNit("AccountingSupplierParty"),
            CustomerNit: PartyNit("AccountingCustomerParty"),
            Environment: (DianEnvironment)int.Parse(root.Element(Cbc + "ProfileExecutionID")!.Value,
                System.Globalization.CultureInfo.InvariantCulture));
    }
}
