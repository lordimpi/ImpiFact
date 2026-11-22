using System.Xml.Linq;
using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Rounding;

namespace ImpiFact.Dian.Tests.Cufe;

public class CufeCalculatorTests
{
    // CUFE: código único de una factura electrónica; CUDE: el equivalente para notas crédito y débito.
    // Ambos son una huella (hash) de los datos principales del documento más un secreto: la clave técnica
    // de la resolución (CUFE) o el PIN del software (CUDE).
    // Estos son los valores fijos que usó tools/golden-capture al generar los casos de referencia
    // (XML y CUFE producidos por ApiDian, el PHP original, que ImpiFact debe reproducir exactamente).
    private const string TechnicalKey = "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c";
    private const string SoftwarePin = "12345";

    /// <summary>PIN of the support-document software of the capture (SD_SOFTWARE_PIN): the secret of the CUDS.</summary>
    private const string SupportDocumentPin = "54321";

    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";

    /// <summary>
    /// Golden folders of the documents ImpiFact issues today, with the secret of their code: the technical key (CUFE) for
    /// the invoice and its AIU, mandate and transport variants, the software PIN (CUDE) for the notes. The phase 7a
    /// variants still pending use other formulas; GoldenCodeTests (Golden.Tests) recomputes every golden case.
    /// </summary>
    private static readonly Dictionary<string, bool> IssuedTypes = new(StringComparer.Ordinal)
    {
        ["invoice"] = true,
        ["invoice-aiu"] = true,
        ["invoice-mandate"] = true,
        ["invoice-transport"] = true,
        ["credit-note"] = false,
        ["debit-note"] = false,
    };

    public static IEnumerable<TestCaseData> GoldenCases() =>
        GoldenFixtures.All().Where(c => IssuedTypes.ContainsKey(c.DocumentType))
            .Select(c => new TestCaseData(c).SetArgDisplayNames($"{c.DocumentType}/{c.Name}"));

    [TestCaseSource(nameof(GoldenCases))]
    public void Matches_the_code_ApiDian_produced(GoldenFixture fixture)
    {
        var xml = XDocument.Parse(fixture.ExpectedXml).Root!;
        var input = ReadInput(xml, fixture.DocumentType);

        var code = IssuedTypes[fixture.DocumentType]
            ? CufeCalculator.ComputeCufe(input, TechnicalKey)
            : CufeCalculator.ComputeCude(input, SoftwarePin);

        Assert.That(code, Is.EqualTo(fixture.ExpectedCode));
    }

    public static IEnumerable<TestCaseData> SupportDocumentCases() =>
        GoldenFixtures.All().Where(c => c.DocumentType is "support-document" or "sd-credit-note")
            .Select(c => new TestCaseData(c).SetArgDisplayNames($"{c.DocumentType}/{c.Name}"));

    // php-rules 2.1 and 13.3: the CUDS of the support document and its adjustment note carries only the IVA and the
    // support-document software PIN; the seller is the supplier NIT and the company the customer one.
    [TestCaseSource(nameof(SupportDocumentCases))]
    public void Matches_the_cuds_ApiDian_produced(GoldenFixture fixture)
    {
        var input = ReadInput(XDocument.Parse(fixture.ExpectedXml).Root!, fixture.DocumentType);

        Assert.That(CufeCalculator.ComputeCuds(input, SupportDocumentPin), Is.EqualTo(fixture.ExpectedCode));
    }

    [Test]
    public void The_cuds_ignores_the_inc_and_the_ica()
    {
        Assert.That(CufeCalculator.ComputeCuds(Sample() with { Inc = 80m, Ica = 9.66m }, SupportDocumentPin),
            Is.EqualTo(CufeCalculator.ComputeCuds(Sample(), SupportDocumentPin)));
        Assert.That(CufeCalculator.ComputeCuds(Sample(), SupportDocumentPin), Is.Not.EqualTo(CufeCalculator.ComputeCude(Sample(), SupportDocumentPin)));
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

        // IssueTime incluye la diferencia horaria fija de Colombia, p. ej. "10:00:00-05:00"; solo se usa "10:00:00".
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
