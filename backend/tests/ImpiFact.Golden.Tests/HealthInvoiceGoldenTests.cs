using System.Globalization;
using System.Text;
using System.Text.Json;
using ImpiFact.Dian.Ubl;

namespace ImpiFact.Golden.Tests;

/// <summary>
/// Health-sector invoices (phase 7a, T7). ApiDian Community ignores <c>health_fields</c> (php-rules 17, items 1 and 2), so
/// each expected.xml is a plain national invoice: it pins the bytes ImpiFact shares with the PHP. The health additions are
/// not from the PHP: they are derived here, in the open, from the Ministerio de Salud's Documento Técnico 2 v001 (15-jul-2026,
/// adopted by Resolución 948 de 2026) and written into the expected text by <see cref="WithHealthAdditions"/>:
/// cbc:CustomizationID with the health operation (section 9), cac:InvoicePeriod right after cbc:LineCountNumeric (section
/// 7.a; the position of ApiDian's template 01, lines 42-45) and, when the operation carries it, the health extension as the
/// second ext:UBLExtension (section 6.a). The CUFE is the PHP one: none of those elements enters the CUFE.
/// </summary>
public sealed class HealthInvoiceGoldenTests
{
    /// <summary>The tests/golden folder covered here.</summary>
    internal const string Type = "invoice-health";

    private static readonly ApiDianCatalog Catalog = ApiDianCatalog.Load();

    /// <summary>
    /// Health operation types in the order of DIAN annex 1.8, table 13.1.5.1.1, and of the Documento Técnico 2, section 9;
    /// <c>health_type_operation_id</c> is the position in that table (ImpiFact's catalog, php-rules 17).
    /// </summary>
    private static readonly string[] OperationCodes = ["SS-CUFE", "SS-CUDE", "SS-POS", "SS-SNum", "SS-Recaudo", "SS-Reporte", "SS-SinAporte"];

    public static IEnumerable<TestCaseData> Cases() =>
        GoldenCases.OfType(Type).Select(dir => new TestCaseData(dir).SetArgDisplayNames(Type + "/" + Path.GetFileName(dir)));

    [TestCaseSource(nameof(Cases))]
    public void Builder_matches_ApiDian_bytes_plus_the_documented_health_additions(string caseDir)
    {
        using var input = Input(caseDir);
        var health = input.RootElement.GetProperty("health_fields");
        if (health.GetProperty("users_info").GetArrayLength() > 1)
        {
            // Documento Técnico 2 v001, section 6.a: the extension has one Collection «Usuario» (1..1), and sections 4.2 and
            // 4.3: every user of a multi-user invoice shares the payment method and the coverage. These two users do not
            // (methods 2 and 3, coverages 5 and 3) and the first one sends a contract and a policy, which section 6.a
            // forbids together. ImpiFact answers 422 to this request (HealthInvoiceIssueApiTests); there is no valid health
            // XML to compare, and inventing one would fake parity.
            Assert.Ignore("The PHP request has two users that disagree on payment method and coverage: not representable under " +
                          "the Documento Técnico 2 v001 (one Collection per invoice); the API rejects it with 422.");
        }

        var invoice = MapHealth(input.RootElement);

        var result = InvoiceUblBuilder.Build(invoice);

        Assert.That(result.Cufe, Is.EqualTo(File.ReadAllText(Path.Combine(caseDir, "expected.cufe")).Trim()), "CUFE");
        var expected = WithHealthAdditions(Encoding.UTF8.GetString(InvoiceGoldenTests.ExpectedBeforeSigning(caseDir)), invoice.HealthSector!, extension: null);
        if (expected != result.Xml)
        {
            Assert.Fail(InvoiceGoldenTests.DescribeFirstDifference(expected, result.Xml));
        }
    }

    // Documento Técnico 2 v001, section 6.a, transcribed element by element: the extension of an invoice with one user, a
    // contract and no policy. The input is the capitation case with operation SS-CUFE and that user.
    [Test]
    public void The_health_extension_follows_the_documento_tecnico_2()
    {
        var caseDir = GoldenCases.OfType(Type).Single(dir => Path.GetFileName(dir) == "capitation-without-users");
        using var input = Input(caseDir);
        var fields = new HealthSectorFields(
            "1100100001", new HealthCode("04", "Pago por evento"), new HealthCode("16", "Plan de beneficios en salud financiado con UPC Contributivo"),
            "CUCON-0001", null, null);
        var invoice = MapHealth(input.RootElement) with
        {
            HealthSector = new HealthSectorTerms("SS-CUFE", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 14), fields),
        };

        var xml = InvoiceUblBuilder.Build(invoice).Xml;

        const string extension =
            "    <ext:UBLExtension>\n" +
            "      <ext:ExtensionContent>\n" +
            "        <CustomTagGeneral>\n" +
            "          <Name>Responsable</Name>\n" +
            "          <Value>url www.minsalud.gov.co</Value>\n" +
            "          <Name>Tipo, identificador:año del acto administrativo</Name>\n" +
            "          <Value>Resolución 0948:2026</Value>\n" +
            "          <Interoperabilidad>\n" +
            "            <Group schemeName=\"Sector Salud\">\n" +
            "              <Collection schemeName=\"Usuario\">\n" +
            "                <AdditionalInformation>\n" +
            "                  <Name>CODIGO_PRESTADOR</Name>\n" +
            "                  <Value>1100100001</Value>\n" +
            "                </AdditionalInformation>\n" +
            "                <AdditionalInformation>\n" +
            "                  <Name>MODALIDAD_PAGO</Name>\n" +
            "                  <Value schemeName=\"salud_modalidad_pago.gc\" schemeID=\"04\">Pago por evento</Value>\n" +
            "                </AdditionalInformation>\n" +
            "                <AdditionalInformation>\n" +
            "                  <Name>COBERTURA_PLAN_BENEFICIOS</Name>\n" +
            "                  <Value schemeName=\"salud_cobertura.gc\" schemeID=\"16\">Plan de beneficios en salud financiado con UPC Contributivo</Value>\n" +
            "                </AdditionalInformation>\n" +
            "                <AdditionalInformation>\n" +
            "                  <Name>NUMERO_CONTRATO</Name>\n" +
            "                  <Value>CUCON-0001</Value>\n" +
            "                </AdditionalInformation>\n" +
            "                <AdditionalInformation>\n" +
            "                  <Name>NUMERO_POLIZA</Name>\n" +
            "                  <Value/>\n" +
            "                </AdditionalInformation>\n" +
            "              </Collection>\n" +
            "            </Group>\n" +
            "          </Interoperabilidad>\n" +
            "        </CustomTagGeneral>\n" +
            "      </ext:ExtensionContent>\n" +
            "    </ext:UBLExtension>\n";
        var expected = WithHealthAdditions(Encoding.UTF8.GetString(InvoiceGoldenTests.ExpectedBeforeSigning(caseDir)), invoice.HealthSector!, extension);
        if (expected != xml)
        {
            Assert.Fail(InvoiceGoldenTests.DescribeFirstDifference(expected, xml));
        }
    }

    // Section 6.a: FACTURA_SIN_CONTRATO is the sixth AdditionalInformation, with the table name and code as attributes.
    [Test]
    public void An_invoice_without_contract_writes_its_justification_after_the_policy()
    {
        var invoice = CapitationAs("SS-CUFE");
        invoice = invoice with
        {
            HealthSector = invoice.HealthSector! with
            {
                Fields = new HealthSectorFields("1100100001", new HealthCode("04", "Pago por evento"), new HealthCode("15", "Particular"),
                    null, null, new HealthCode("07", "ATENCION A PACIENTES PARTICULARES")),
            },
        };

        var xml = InvoiceUblBuilder.Build(invoice).Xml;

        Assert.That(xml, Does.Contain(
            "<Name>NUMERO_CONTRATO</Name>\n                  <Value/>\n                </AdditionalInformation>\n" +
            "                <AdditionalInformation>\n                  <Name>NUMERO_POLIZA</Name>\n                  <Value/>\n" +
            "                </AdditionalInformation>\n                <AdditionalInformation>\n" +
            "                  <Name>FACTURA_SIN_CONTRATO</Name>\n" +
            "                  <Value schemeName=\"salud_facturaSinContrato.gc\" schemeID=\"07\">ATENCION A PACIENTES PARTICULARES</Value>\n" +
            "                </AdditionalInformation>\n              </Collection>"));
    }

    // Section 7.c: in a health invoice every cac:PrepaidPayment says its collection concept as the schemeID of cbc:ID
    // (table conceptoRecaudo); a plain invoice keeps cbc:ID without attributes (php-rules 3.2).
    [Test]
    public void A_health_prepaid_payment_carries_its_concept_as_scheme_id()
    {
        var prepaid = new PrepaidPayment("1", 5000m, new DateOnly(2026, 11, 14), null, null, "02");
        var health = CapitationAs("SS-CUFE") with { PrepaidPayments = [prepaid] };
        var plain = health with { HealthSector = null, PrepaidPayments = [prepaid with { ConceptCode = null }] };

        Assert.That(InvoiceUblBuilder.Build(health).Xml, Does.Contain(
            "<cac:PrepaidPayment>\n    <cbc:ID schemeID=\"02\">1</cbc:ID>\n    <cbc:PaidAmount currencyID=\"COP\">5000.00</cbc:PaidAmount>"));
        Assert.That(InvoiceUblBuilder.Build(plain).Xml, Does.Contain("<cac:PrepaidPayment>\n    <cbc:ID>1</cbc:ID>"));
    }

    // Without health data the invoice is the PHP one: no InvoicePeriod, no extension, operation 10.
    [Test]
    public void Without_health_data_the_invoice_keeps_the_ApiDian_bytes()
    {
        var caseDir = GoldenCases.OfType(Type).Single(dir => Path.GetFileName(dir) == "capitation-without-users");
        var plain = CapitationAs("SS-Recaudo") with { HealthSector = null };

        var xml = InvoiceUblBuilder.Build(plain).Xml;

        Assert.That(xml, Is.EqualTo(Encoding.UTF8.GetString(InvoiceGoldenTests.ExpectedBeforeSigning(caseDir))));
    }

    /// <summary>
    /// The PHP bytes with the health additions: the operation in cbc:CustomizationID, cac:InvoicePeriod after
    /// cbc:LineCountNumeric and, when given, the extension text before the (empty) signature extension.
    /// </summary>
    private static string WithHealthAdditions(string phpXml, HealthSectorTerms health, string? extension)
    {
        var xml = ReplaceOnce(phpXml, "<cbc:CustomizationID>10</cbc:CustomizationID>", $"<cbc:CustomizationID>{health.OperationCode}</cbc:CustomizationID>");
        var lineCountEnd = xml.IndexOf("</cbc:LineCountNumeric>\n", StringComparison.Ordinal) + "</cbc:LineCountNumeric>\n".Length;
        xml = xml.Insert(lineCountEnd,
            "  <cac:InvoicePeriod>\n" +
            $"    <cbc:StartDate>{health.PeriodStartDate:yyyy-MM-dd}</cbc:StartDate>\n" +
            $"    <cbc:EndDate>{health.PeriodEndDate:yyyy-MM-dd}</cbc:EndDate>\n" +
            "  </cac:InvoicePeriod>\n");
        if (extension is not null)
        {
            const string signatureExtension = "    <ext:UBLExtension>\n      <ext:ExtensionContent/>\n";
            xml = xml.Insert(xml.IndexOf(signatureExtension, StringComparison.Ordinal), extension);
        }

        return xml;
    }

    private static string ReplaceOnce(string text, string oldValue, string newValue)
    {
        var index = text.IndexOf(oldValue, StringComparison.Ordinal);
        Assert.That(index, Is.GreaterThanOrEqualTo(0), oldValue);
        return string.Concat(text.AsSpan(0, index), newValue, text.AsSpan(index + oldValue.Length));
    }

    private static SalesInvoice CapitationAs(string operationCode)
    {
        using var input = Input(GoldenCases.OfType(Type).Single(dir => Path.GetFileName(dir) == "capitation-without-users"));
        var invoice = MapHealth(input.RootElement);
        return invoice with { HealthSector = invoice.HealthSector! with { OperationCode = operationCode } };
    }

    /// <summary>
    /// The plain invoice the PHP wrote plus the health terms of <c>health_fields</c>: the operation of
    /// <c>health_type_operation_id</c> and the period. The golden inputs that reach this method have no users.
    /// </summary>
    private static SalesInvoice MapHealth(JsonElement request)
    {
        var health = request.GetProperty("health_fields");
        return new ApiDianInvoiceAdapter(Catalog).Map(request) with
        {
            HealthSector = new HealthSectorTerms(
                OperationCodes[health.GetProperty("health_type_operation_id").GetInt32() - 1],
                DateOnly.ParseExact(health.GetProperty("invoice_period_start_date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateOnly.ParseExact(health.GetProperty("invoice_period_end_date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Fields: null),
        };
    }

    private static JsonDocument Input(string caseDir) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "input.json"), Encoding.UTF8));
}
