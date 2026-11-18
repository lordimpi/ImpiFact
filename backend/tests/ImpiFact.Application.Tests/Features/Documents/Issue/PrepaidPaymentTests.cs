using System.Text;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;

namespace ImpiFact.Application.Tests.Features.Documents.Issue;

/// <summary>
/// Anticipos de la factura (<c>prepaid_payment</c> y <c>prepaid_payments</c>, como en <c>InvoiceController.php</c>):
/// cada uno es un cac:PrepaidPayment del XML, su suma es <c>pre_paid_amount</c> (regla FAU12 del anexo técnico de la
/// DIAN) y no cambian el valor a pagar ni el CUFE (código único de la factura electrónica).
/// </summary>
public sealed class PrepaidPaymentTests
{
    private const string Single = """
        {"idpayment":"A3123856","paidamount":"100000.00","receiveddate":"2026-11-01","paiddate":"2026-11-02","instructionid":"PRUEBA DE PREPAGO RECIBIDO"}
        """;

    [Test]
    public async Task Prepaid_payments_are_written_in_the_xml_and_balance_the_prepaid_total()
    {
        using var harness = IssueHarness.Create();
        var input = WithPrepaid(json =>
        {
            json["prepaid_payment"] = JsonNode.Parse(Single);
            json["prepaid_payments"] = JsonNode.Parse("""[{"idpayment":"B-2","paidamount":50000.5,"receiveddate":"2026-11-03"}]""");
            Totals(json)["pre_paid_amount"] = "150000.50";
        });

        var result = await harness.IssueAsync("invoice", input);

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        var xml = Unsigned(result);
        Assert.That(xml, Does.Contain(
            "  </cac:PaymentTerms>\n" +
            "  <cac:PrepaidPayment>\n" +
            "    <cbc:ID>A3123856</cbc:ID>\n" +
            "    <cbc:PaidAmount currencyID=\"COP\">100000.00</cbc:PaidAmount>\n" +
            "    <cbc:ReceivedDate>2026-11-01</cbc:ReceivedDate>\n" +
            "    <cbc:PaidDate>2026-11-02</cbc:PaidDate>\n" +
            "    <cbc:InstructionID>PRUEBA DE PREPAGO RECIBIDO</cbc:InstructionID>\n" +
            "  </cac:PrepaidPayment>\n" +
            "  <cac:PrepaidPayment>\n" +
            "    <cbc:ID>B-2</cbc:ID>\n" +
            "    <cbc:PaidAmount currencyID=\"COP\">50000.50</cbc:PaidAmount>\n" +
            "    <cbc:ReceivedDate>2026-11-03</cbc:ReceivedDate>\n" +
            "  </cac:PrepaidPayment>\n"));
        Assert.That(xml, Does.Contain("<cbc:PrepaidAmount currencyID=\"COP\">150000.50</cbc:PrepaidAmount>"));
        Assert.That(harness.SingleGraph.Document.PrepaidAmount, Is.EqualTo(150000.50m));
    }

    [Test]
    public async Task Prepaid_payments_do_not_change_the_cufe()
    {
        using var plainHarness = IssueHarness.Create();
        using var prepaidHarness = IssueHarness.Create();

        var plain = await plainHarness.IssueAsync("invoice", IssueHarness.GoldenInput(IssueHarness.FirstInvoice));
        var prepaid = await prepaidHarness.IssueAsync("invoice", WithPrepaid(json =>
        {
            json["prepaid_payment"] = JsonNode.Parse(Single);
            Totals(json)["pre_paid_amount"] = "100000.00";
        }));

        Assert.That(prepaid.IsSuccess, Is.True, Describe(prepaid));
        Assert.That(prepaid.Value.Cufe, Is.EqualTo(plain.Value.Cufe));
    }

    [Test]
    public async Task Prepaid_total_different_from_the_sum_of_the_prepaid_payments_is_rejected_by_fau12()
    {
        using var harness = IssueHarness.Create();
        var input = WithPrepaid(json =>
        {
            json["prepaid_payment"] = JsonNode.Parse(Single);
            Totals(json)["pre_paid_amount"] = "90000.00";
        });

        var result = await harness.IssueAsync("invoice", input);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error.Fields.Select(field => field.Field), Does.Contain("legal_monetary_totals.pre_paid_amount"), Describe(result));
        Assert.That(string.Join(" ", result.Error.Fields.Select(field => field.Message)), Does.Contain("FAU12"));
    }

    // El ejemplo «Factura con anticipo» de la colección Postman resta el anticipo del valor a pagar; la regla FAU14 del
    // anexo 1.9 no lo resta (total con impuestos − descuentos + cargos), así que la DIAN lo rechazaría.
    [Test]
    public async Task Subtracting_the_prepaid_amount_from_the_payable_amount_is_rejected_by_fau14_only()
    {
        using var harness = IssueHarness.Create();
        var input = WithPrepaid(json =>
        {
            json["prepaid_payment"] = JsonNode.Parse(Single);
            Totals(json)["pre_paid_amount"] = "100000.00";
            Totals(json)["payable_amount"] = "1685000.00";
        });

        var result = await harness.IssueAsync("invoice", input);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error.Fields.Select(field => field.Field), Is.EqualTo(new[] { "legal_monetary_totals.payable_amount" }), Describe(result));
        Assert.That(result.Error.Fields.Single().Message, Does.Contain("FAU14"));
    }

    // php-rules 3.1, ítem 6: pre_paid_amount es obligatorio cuando llega prepaid_payment (required_with).
    [Test]
    public async Task A_prepaid_payment_without_a_prepaid_total_is_rejected()
    {
        using var harness = IssueHarness.Create();
        var input = WithPrepaid(json => json["prepaid_payment"] = JsonNode.Parse(Single));

        var result = await harness.IssueAsync("invoice", input);

        AssertValidation(result, "legal_monetary_totals.pre_paid_amount",
            "El campo legal monetary totals.pre paid amount es obligatorio cuando prepaid payment está presente.");
    }

    // Anexo técnico 1.9, grupo FBD: ID (FBD02), PaidAmount (FBD03) y ReceivedDate (FBD05) son obligatorios.
    [Test]
    public async Task A_prepaid_payment_needs_its_id_amount_and_received_date()
    {
        using var harness = IssueHarness.Create();
        var input = WithPrepaid(json =>
        {
            json["prepaid_payments"] = JsonNode.Parse("""[{"instructionid":"SIN DATOS"}]""");
            Totals(json)["pre_paid_amount"] = "0.00";
        });

        var result = await harness.IssueAsync("invoice", input);

        AssertValidation(result, "prepaid_payments.0.idpayment", "El campo prepaid payments.0.idpayment es obligatorio.");
        AssertValidation(result, "prepaid_payments.0.paidamount", "El campo prepaid payments.0.paidamount es obligatorio.");
        AssertValidation(result, "prepaid_payments.0.receiveddate", "El campo prepaid payments.0.receiveddate es obligatorio.");
    }

    [Test]
    public async Task Prepaid_payment_values_keep_the_php_formats()
    {
        using var harness = IssueHarness.Create();
        var input = WithPrepaid(json =>
        {
            json["prepaid_payment"] = JsonNode.Parse("""{"idpayment":"A1","paidamount":"diez","receiveddate":"01/11/2026","paiddate":"2026-13-01"}""");
            Totals(json)["pre_paid_amount"] = "10.00";
        });

        var result = await harness.IssueAsync("invoice", input);

        AssertValidation(result, "prepaid_payment.paidamount", "prepaid payment.paidamount debe ser numérico.");
        AssertValidation(result, "prepaid_payment.receiveddate", "prepaid payment.receiveddate no corresponde al formato Y-m-d.");
        AssertValidation(result, "prepaid_payment.paiddate", "prepaid payment.paiddate no corresponde al formato Y-m-d.");
    }

    // Como el PHP (if($request->prepaid_payment)), un objeto vacío es como no enviar anticipo.
    [Test]
    public async Task An_empty_prepaid_payment_object_is_ignored()
    {
        using var harness = IssueHarness.Create();
        var input = WithPrepaid(json => json["prepaid_payment"] = new JsonObject());

        var result = await harness.IssueAsync("invoice", input);

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(Unsigned(result), Does.Not.Contain("PrepaidPayment"));
    }

    // Las plantillas de las notas (91.blade.php y 92.blade.php) no escriben anticipos: el PHP los ignora.
    [TestCase("credit-note")]
    [TestCase("debit-note")]
    public async Task Notes_ignore_prepaid_payments_like_the_php(string kind)
    {
        using var harness = IssueHarness.Create();
        var caseDir = kind == "credit-note" ? IssueHarness.FirstCreditNote : IssueHarness.FirstDebitNote;
        var input = IssueHarness.Edited(caseDir, json => json["prepaid_payment"] = JsonNode.Parse(Single));

        var result = await harness.IssueAsync(kind, input);

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(Unsigned(result), Does.Not.Contain("PrepaidPayment"));
    }

    private static byte[] WithPrepaid(Action<JsonObject> change) => IssueHarness.Edited(IssueHarness.FirstInvoice, change);

    private static JsonObject Totals(JsonObject json) => json["legal_monetary_totals"]!.AsObject();

    private static string Unsigned(Result<IssueDocumentResponse> result) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(result.Value.UnsignedInvoiceXml!));

    private static void AssertValidation(Result<IssueDocumentResponse> result, string field, string message)
    {
        Assert.That(result.IsSuccess, Is.False, "Expected a validation error.");
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
        Assert.That(result.Error.Fields, Does.Contain(new FieldError(field, message)), string.Join(" | ", result.Error.Fields));
    }

    [Test]
    public async Task A_negative_prepaid_amount_is_reported_on_its_own_path()
    {
        using var harness = IssueHarness.Create();
        var input = WithPrepaid(json =>
        {
            json["prepaid_payment"] = JsonNode.Parse(Single);
            json["prepaid_payments"] = JsonNode.Parse("""[{"idpayment":"N","paidamount":"-5000","receiveddate":"2026-11-03"}]""");
            Totals(json)["pre_paid_amount"] = "95000.00";
        });

        var result = await harness.IssueAsync("invoice", input);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error.Fields.Select(f => f.Field), Does.Contain("prepaid_payments.0.paidamount"), Describe(result));
    }

    private static string Describe(Result<IssueDocumentResponse> result) =>
        result.IsSuccess ? "" : $"{result.Error.Code}: {result.Error.Message} {string.Join(" | ", result.Error.Fields)}";
}
