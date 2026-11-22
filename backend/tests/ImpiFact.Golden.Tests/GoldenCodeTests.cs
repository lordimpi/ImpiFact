using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace ImpiFact.Golden.Tests;

/// <summary>
/// Revisión estructural de todos los casos de referencia, tengan o no builder en ImpiFact: el código de expected.cufe
/// (CUFE, CUDE o CUDS) es el cbc:UUID de expected.xml y sale de recalcularlo con los valores del propio XML y la
/// configuración fija de captura. Protege los golden de ediciones a mano. La firma no se puede revisar: capture.py la
/// quita de expected.xml.
/// </summary>
/// <remarks>
/// La fórmula es la que exige la DIAN para el schemeName del cbc:UUID (el elemento raíz ApplicationResponse indica un
/// evento). Cuando ApiDian usó otra (un error del PHP que el golden conserva tal cual), capture.py lo anota en el
/// code_check del case.json del caso, y la prueba recalcula con la fórmula que usó el PHP.
/// </remarks>
public sealed class GoldenCodeTests
{
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";

    private static readonly Dictionary<string, string> Secrets = new(StringComparer.Ordinal)
    {
        ["technical key"] = GoldenCaptureContext.TechnicalKey,
        ["software PIN"] = GoldenCaptureContext.SoftwarePin,
        ["support-document software PIN"] = GoldenCaptureContext.SupportDocumentSoftwarePin,
    };

    public static IEnumerable<TestCaseData> Cases() =>
        GoldenCases.TypeFolders()
            .SelectMany(type => GoldenCases.OfType(type)
                .Where(dir => File.Exists(Path.Combine(dir, "expected.cufe")))
                .Select(dir => new TestCaseData(dir).SetArgDisplayNames(type + "/" + Path.GetFileName(dir))));

    [TestCaseSource(nameof(Cases))]
    public void Expected_code_is_the_uuid_of_the_expected_xml(string caseDir)
    {
        var root = LoadExpected(caseDir);

        Assert.That(Child(root, Cbc + "UUID"), Is.EqualTo(ExpectedCode(caseDir)));
    }

    [TestCaseSource(nameof(Cases))]
    public void Expected_code_matches_its_recomputation_from_the_xml(string caseDir)
    {
        var root = LoadExpected(caseDir);
        var (formula, secret) = FormulaUsedByApiDian(caseDir, root);

        Assert.That(Recompute(root, formula, Secrets[secret]), Is.EqualTo(ExpectedCode(caseDir)), $"{formula} with the {secret}");
    }

    private static (string Formula, string Secret) FormulaUsedByApiDian(string caseDir, XElement root)
    {
        var required = RequiredFormula(root);
        var metadataPath = Path.Combine(caseDir, "case.json");
        if (!File.Exists(metadataPath))
        {
            return required;
        }

        using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath, Encoding.UTF8));
        var check = metadata.RootElement.GetProperty("code_check");
        Assert.That((check.GetProperty("formula").GetString(), check.GetProperty("secret").GetString()), Is.EqualTo(required),
            "code_check in case.json must name the formula the DIAN requires");
        return check.GetProperty("matches").GetBoolean()
            ? required
            : (check.GetProperty("php_formula").GetString()!, check.GetProperty("php_secret").GetString()!);
    }

    private static (string Formula, string Secret) RequiredFormula(XElement root)
    {
        if (root.Name.LocalName == "ApplicationResponse")
        {
            return ("EVENT", "software PIN");
        }

        return root.Element(Cbc + "UUID")?.Attribute("schemeName")?.Value switch
        {
            "CUFE-SHA384" => ("CUFE", "technical key"),
            "CUDE-SHA384" => ("CUDE", "software PIN"),
            "CUDS-SHA384" => ("CUDS", "support-document software PIN"),
            var other => throw new AssertionException($"Unknown cbc:UUID schemeName {other}"),
        };
    }

    // Mismas fórmulas que check_code en tools/golden-capture/capture.py (anexo técnico de la DIAN): el SHA-384 de la
    // concatenación de valores del XML. Cada impuesto va como código + valor (01 IVA, 04 INC, 03 ICA), con 0.00 si
    // el documento no lo tiene; el CUDS solo lleva el IVA. ContingencyGoldenTests also recomputes ImpiFact's own code.
    internal static string Recompute(XElement root, string formula, string secret)
    {
        var parts = new List<string> { Child(root, Cbc + "ID"), Child(root, Cbc + "IssueDate"), Child(root, Cbc + "IssueTime") };
        if (formula == "EVENT")
        {
            var response = root.Element(Cac + "DocumentResponse")!;
            parts.AddRange(
            [
                Child(root.Element(Cac + "SenderParty")!.Element(Cac + "PartyTaxScheme")!, Cbc + "CompanyID"),
                Child(root.Element(Cac + "ReceiverParty")!.Element(Cac + "PartyTaxScheme")!, Cbc + "CompanyID"),
                Child(response.Element(Cac + "Response")!, Cbc + "ResponseCode"),
                Child(response.Element(Cac + "DocumentReference")!, Cbc + "ID"),
                Child(response.Element(Cac + "DocumentReference")!, Cbc + "DocumentTypeCode"),
                secret,
            ]);
            return Sha384(parts);
        }

        var totals = root.Element(Cac + "LegalMonetaryTotal") ?? root.Element(Cac + "RequestedMonetaryTotal")!;
        var taxes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var taxTotal in root.Elements(Cac + "TaxTotal"))
        {
            var code = taxTotal.Element(Cac + "TaxSubtotal")?.Element(Cac + "TaxCategory")?.Element(Cac + "TaxScheme")?.Element(Cbc + "ID")?.Value ?? "";
            taxes.TryAdd(code, Child(taxTotal, Cbc + "TaxAmount"));
        }

        parts.Add(Child(totals, Cbc + "LineExtensionAmount"));
        parts.AddRange(["01", taxes.GetValueOrDefault("01", "0.00")]);
        if (formula != "CUDS")
        {
            parts.AddRange(["04", taxes.GetValueOrDefault("04", "0.00"), "03", taxes.GetValueOrDefault("03", "0.00")]);
        }

        parts.AddRange(
        [
            Child(totals, Cbc + "PayableAmount"),
            PartyCompanyId(root, "AccountingSupplierParty"),
            PartyCompanyId(root, "AccountingCustomerParty"),
            secret,
            Child(root, Cbc + "ProfileExecutionID"),
        ]);
        return Sha384(parts);
    }

    private static string PartyCompanyId(XElement root, string party) =>
        Child(root.Element(Cac + party)!.Element(Cac + "Party")!.Element(Cac + "PartyTaxScheme")!, Cbc + "CompanyID");

    private static string Child(XElement parent, XName name) =>
        parent.Element(name)?.Value ?? throw new AssertionException($"{name.LocalName} not found under {parent.Name.LocalName}");

    private static string Sha384(IEnumerable<string> parts) =>
        Convert.ToHexStringLower(SHA384.HashData(Encoding.UTF8.GetBytes(string.Concat(parts))));

    private static string ExpectedCode(string caseDir) => File.ReadAllText(Path.Combine(caseDir, "expected.cufe")).Trim();

    private static XElement LoadExpected(string caseDir)
    {
        using var reader = XmlReader.Create(Path.Combine(caseDir, "expected.xml"),
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader).Root!;
    }
}
