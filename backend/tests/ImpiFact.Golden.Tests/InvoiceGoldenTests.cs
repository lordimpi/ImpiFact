using System.Text;
using System.Text.Json;
using ImpiFact.Dian.Ubl;

namespace ImpiFact.Golden.Tests;

/// <summary>
/// Casos de referencia de ApiDian (XML y CUFE generados por el PHP original): al convertir input.json con
/// la configuración fija de captura, ImpiFact debe producir los mismos bytes que expected.xml y el mismo
/// CUFE (código único de la factura) que expected.cufe.
/// </summary>
public sealed class InvoiceGoldenTests
{
    /// <summary>
    /// expected.xml es el XML firmado por ApiDian al que se le quitó el elemento ds:Signature con una
    /// expresión regular. Eso deja el lugar reservado para la firma (el segundo ext:ExtensionContent)
    /// escrito con etiqueta de apertura y de cierre. El XML que produce ImpiFact antes de firmar tiene ese
    /// elemento vacío y lo escribe <c>&lt;ext:ExtensionContent/&gt;</c>, como cualquier otro elemento vacío.
    /// Este es el único ajuste que se hace al comparar, y solo en ese punto.
    /// </summary>
    internal const string SignatureSlotInFixture = "<ext:ExtensionContent></ext:ExtensionContent>";

    internal const string SignatureSlotBeforeSigning = "<ext:ExtensionContent/>";

    private static readonly ApiDianCatalog Catalog = ApiDianCatalog.Load();

    public static IEnumerable<TestCaseData> Cases() =>
        GoldenCases.Invoices().Select(dir => new TestCaseData(dir).SetArgDisplayNames("invoice/" + Path.GetFileName(dir)));

    [TestCaseSource(nameof(Cases))]
    public void Builder_matches_ApiDian_xml_and_cufe(string caseDir)
    {
        var result = Build(caseDir);

        var expectedCufe = File.ReadAllText(Path.Combine(caseDir, "expected.cufe")).Trim();
        Assert.That(result.Cufe, Is.EqualTo(expectedCufe), "CUFE");

        var expected = ExpectedBeforeSigning(caseDir);
        var actual = new UTF8Encoding(false).GetBytes(result.Xml);
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            Assert.Fail(DescribeFirstDifference(Encoding.UTF8.GetString(expected), result.Xml));
        }
    }

    // La URL del QR llega en el modelo de entrada (viene de la configuración por ambiente), no fija en el código.
    [Test]
    public void Qr_code_uses_the_base_url_given_in_the_input()
    {
        var caseDir = GoldenCases.Invoices().First();
        using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "input.json"), Encoding.UTF8));
        var invoice = new ApiDianInvoiceAdapter(Catalog).Map(input.RootElement) with
        {
            QrCodeBaseUrl = "https://qr.example.test/search?documentkey=",
        };

        var result = InvoiceUblBuilder.Build(invoice);

        Assert.That(result.Xml, Does.Contain($"<sts:QRCode>https://qr.example.test/search?documentkey={result.Cufe}</sts:QRCode>"));
    }

    // Como el PHP (01.blade.php), la nota de la factura va justo después de cbc:InvoiceTypeCode y solo si llega.
    [Test]
    public void Invoice_note_goes_right_after_the_invoice_type_code()
    {
        var caseDir = GoldenCases.Invoices().First();
        using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "input.json"), Encoding.UTF8));
        var invoice = new ApiDianInvoiceAdapter(Catalog).Map(input.RootElement) with { Note = "Entrega en bodega" };

        var xml = InvoiceUblBuilder.Build(invoice).Xml;

        Assert.That(xml, Does.Contain("</cbc:InvoiceTypeCode>\n  <cbc:Note>Entrega en bodega</cbc:Note>\n  <cbc:DocumentCurrencyCode>"));
    }

    // No hay caso golden de ApiDian con anticipos: lo esperado sale de las plantillas del PHP
    // (01.blade.php incluye _prepaid_payment y luego _prepaid_payments justo después de cac:PaymentTerms y antes de
    // cac:AllowanceCharge; _legal_monetary_total escribe cbc:PrepaidAmount antes de cbc:PayableAmount) y del anexo
    // técnico 1.9 (grupo FBD: ID, PaidAmount, ReceivedDate, PaidDate e InstructionID, en ese orden).
    [Test]
    public void Prepaid_payments_go_after_the_payment_terms_with_the_php_layout()
    {
        var invoice = Prepaid(GoldenInvoice("global-charge-credit-payment"));

        var xml = InvoiceUblBuilder.Build(invoice).Xml;

        Assert.That(xml, Does.Contain(
            "  </cac:PaymentTerms>\n" +
            "  <cac:PrepaidPayment>\n" +
            "    <cbc:ID>A3123856</cbc:ID>\n" +
            "    <cbc:PaidAmount currencyID=\"COP\">100000.00</cbc:PaidAmount>\n" +
            "    <cbc:ReceivedDate>2020-03-01</cbc:ReceivedDate>\n" +
            "    <cbc:PaidDate>2020-03-05</cbc:PaidDate>\n" +
            "    <cbc:InstructionID>PRUEBA DE PREPAGO RECIBIDO</cbc:InstructionID>\n" +
            "  </cac:PrepaidPayment>\n" +
            "  <cac:PrepaidPayment>\n" +
            "    <cbc:ID>B-2</cbc:ID>\n" +
            "    <cbc:PaidAmount currencyID=\"COP\">15022.50</cbc:PaidAmount>\n" +
            "    <cbc:ReceivedDate>2020-03-02</cbc:ReceivedDate>\n" +
            "  </cac:PrepaidPayment>\n" +
            "  <cac:AllowanceCharge>\n"));
        Assert.That(xml, Does.Contain(
            "    <cbc:PrepaidAmount currencyID=\"COP\">115022.50</cbc:PrepaidAmount>\n" +
            "    <cbc:PayableAmount currencyID=\"COP\">1615022.50</cbc:PayableAmount>\n"));
    }

    // Anexo técnico 1.9, sección del CUFE: entra ValTot (cbc:PayableAmount), no los anticipos; el valor a pagar no
    // cambia con ellos (regla FAU14), así que el CUFE es el mismo con o sin anticipos.
    [Test]
    public void Prepaid_payments_do_not_change_the_cufe()
    {
        var invoice = GoldenInvoice("global-charge-credit-payment");

        var plain = InvoiceUblBuilder.Build(invoice);
        var prepaid = InvoiceUblBuilder.Build(Prepaid(invoice));

        Assert.That(prepaid.Cufe, Is.EqualTo(plain.Cufe));
    }

    // Como las retenciones (php-rules, sección 3.2, ítem 6) y la regla FBD04 del anexo: el anticipo va en la moneda
    // del documento (cbc:DocumentCurrencyCode), la extranjera si la hay.
    [Test]
    public void Prepaid_payments_use_the_document_currency()
    {
        var invoice = Prepaid(GoldenInvoice("global-charge-credit-payment")) with
        {
            ForeignCurrency = new ForeignCurrency("USD", 4000m, new DateOnly(2020, 3, 1)),
        };

        var xml = InvoiceUblBuilder.Build(invoice).Xml;

        Assert.That(xml, Does.Contain("<cbc:PaidAmount currencyID=\"USD\">100000.00</cbc:PaidAmount>"));
        Assert.That(xml, Does.Contain("<cbc:PrepaidAmount currencyID=\"COP\">115022.50</cbc:PrepaidAmount>"));
    }

    [Test]
    public void An_invoice_without_prepaid_payments_writes_no_prepaid_payment()
    {
        var xml = InvoiceUblBuilder.Build(GoldenInvoice("global-charge-credit-payment")).Xml;

        Assert.That(xml, Does.Not.Contain("PrepaidPayment"));
    }

    private static SalesInvoice GoldenInvoice(string name)
    {
        var caseDir = GoldenCases.Invoices().Single(dir => Path.GetFileName(dir) == name);
        using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "input.json"), Encoding.UTF8));
        return new ApiDianInvoiceAdapter(Catalog).Map(input.RootElement);
    }

    private static SalesInvoice Prepaid(SalesInvoice invoice) => invoice with
    {
        PrepaidPayments =
        [
            new PrepaidPayment("A3123856", 100000m, new DateOnly(2020, 3, 1), new DateOnly(2020, 3, 5), "PRUEBA DE PREPAGO RECIBIDO"),
            new PrepaidPayment("B-2", 15022.5m, new DateOnly(2020, 3, 2), null, null),
        ],
        Totals = invoice.Totals with { PrepaidAmount = 115022.5m },
    };

    private static InvoiceUblResult Build(string caseDir)
    {
        using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "input.json"), Encoding.UTF8));
        var invoice = new ApiDianInvoiceAdapter(Catalog).Map(input.RootElement);
        return InvoiceUblBuilder.Build(invoice);
    }

    internal static byte[] ExpectedBeforeSigning(string caseDir)
    {
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(caseDir, "expected.xml")));
        var first = text.IndexOf(SignatureSlotInFixture, StringComparison.Ordinal);
        if (first < 0 || text.IndexOf(SignatureSlotInFixture, first + 1, StringComparison.Ordinal) >= 0)
        {
            Assert.Fail($"expected.xml must contain exactly one {SignatureSlotInFixture} (the removed signature)");
        }

        var normalized = string.Concat(text.AsSpan(0, first), SignatureSlotBeforeSigning, text.AsSpan(first + SignatureSlotInFixture.Length));
        return new UTF8Encoding(false).GetBytes(normalized);
    }

    /// <summary>
    /// Informa la primera línea que difiere, con el valor esperado y el obtenido, y muestra los caracteres
    /// invisibles \r y \t como texto.
    /// </summary>
    internal static string DescribeFirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var count = Math.Max(expectedLines.Length, actualLines.Length);
        for (var i = 0; i < count; i++)
        {
            var e = i < expectedLines.Length ? expectedLines[i] : "<end of document>";
            var a = i < actualLines.Length ? actualLines[i] : "<end of document>";
            if (!string.Equals(e, a, StringComparison.Ordinal))
            {
                return $"First difference at line {i + 1}:{Environment.NewLine}" +
                       $"  expected: [{Visible(e)}]{Environment.NewLine}" +
                       $"  actual:   [{Visible(a)}]";
            }
        }

        return "Documents differ only in encoding or line endings";
    }

    private static string Visible(string line) => line.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);
}
