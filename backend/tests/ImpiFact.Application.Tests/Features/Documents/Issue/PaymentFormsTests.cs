using System.Text;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;

namespace ImpiFact.Application.Tests.Features.Documents.Issue;

/// <summary>
/// Varias formas de pago (<c>payment_form</c> como lista, como en <c>InvoiceController.php</c> y en los controladores
/// de las notas): un cac:PaymentMeans por forma, con cbc:PaymentID desde 0; cac:PaymentTerms (solo en la factura)
/// sale de la primera; todas se guardan en <c>document_payments</c>.
/// </summary>
public sealed class PaymentFormsTests
{
    private const string TwoForms = """
        [
          {"payment_form_id": 2, "payment_method_id": 30, "payment_due_date": "2026-12-14", "duration_measure": "30"},
          {"payment_form_id": 1, "payment_method_id": 10}
        ]
        """;

    [Test]
    public async Task An_invoice_with_two_payment_forms_writes_two_payment_means_and_saves_both()
    {
        using var harness = IssueHarness.Create();
        var input = IssueHarness.Edited(IssueHarness.FirstInvoice, json => json["payment_form"] = JsonNode.Parse(TwoForms));

        var result = await harness.IssueAsync("invoice", input);

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(Unsigned(result), Does.Contain(
            "  <cac:PaymentMeans>\n" +
            "    <cbc:ID>2</cbc:ID>\n" +
            "    <cbc:PaymentMeansCode>30</cbc:PaymentMeansCode>\n" +
            "    <cbc:PaymentDueDate>2026-12-14</cbc:PaymentDueDate>\n" +
            "    <cbc:PaymentID>0</cbc:PaymentID>\n" +
            "  </cac:PaymentMeans>\n" +
            "  <cac:PaymentMeans>\n" +
            "    <cbc:ID>1</cbc:ID>\n" +
            "    <cbc:PaymentMeansCode>10</cbc:PaymentMeansCode>\n" +
            "    <cbc:PaymentDueDate>2026-11-14</cbc:PaymentDueDate>\n" +
            "    <cbc:PaymentID>1</cbc:PaymentID>\n" +
            "  </cac:PaymentMeans>\n" +
            "  <cac:PaymentTerms>\n" +
            "    <cbc:ReferenceEventCode>2</cbc:ReferenceEventCode>\n" +
            "    <cac:SettlementPeriod>\n" +
            "      <cbc:DurationMeasure unitCode=\"DAY\">30</cbc:DurationMeasure>\n"));
        var payments = harness.SingleGraph.Payments;
        Assert.That(payments.Select(payment => (payment.PaymentFormId, payment.PaymentMethodId)), Is.EqualTo(new[] { (2, 30), (1, 10) }));
        Assert.That(payments.Select(payment => payment.DueDate), Is.EqualTo(new DateOnly?[] { new DateOnly(2026, 12, 14), null }));
    }

    [TestCase("credit-note")]
    [TestCase("debit-note")]
    public async Task A_note_with_two_payment_forms_writes_two_payment_means(string kind)
    {
        using var harness = IssueHarness.Create();
        var caseDir = kind == "credit-note" ? IssueHarness.FirstCreditNote : IssueHarness.FirstDebitNote;
        var input = IssueHarness.Edited(caseDir, json => json["payment_form"] = JsonNode.Parse(TwoForms));

        var result = await harness.IssueAsync(kind, input);

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        var xml = Unsigned(result);
        Assert.That(CountOf(xml, "<cac:PaymentMeans>"), Is.EqualTo(2));
        Assert.That(xml, Does.Contain("<cbc:PaymentID>1</cbc:PaymentID>"));
        Assert.That(harness.SingleGraph.Payments, Has.Count.EqualTo(2));
    }

    // Con una lista, los errores llevan el índice de la forma de pago (payment_form.1.…), como Laravel.
    [Test]
    public async Task Errors_of_a_payment_form_in_a_list_carry_its_index()
    {
        using var harness = IssueHarness.Create();
        var input = IssueHarness.Edited(IssueHarness.FirstInvoice, json => json["payment_form"] = JsonNode.Parse(
            """[{"payment_form_id": 1, "payment_method_id": 10}, {"payment_form_id": 2, "payment_method_id": 10}]"""));

        var result = await harness.IssueAsync("invoice", input);

        AssertValidation(result, "payment_form.1.payment_due_date",
            "El campo payment form.1.payment due date es obligatorio cuando payment form.1.payment form id es 2.");
        AssertValidation(result, "payment_form.1.duration_measure",
            "El campo payment form.1.duration measure es obligatorio cuando payment form.1.payment form id es 2.");
    }

    [Test]
    public async Task An_unknown_payment_method_in_a_list_is_reported_with_its_index()
    {
        using var harness = IssueHarness.Create();
        var input = IssueHarness.Edited(IssueHarness.FirstInvoice, json => json["payment_form"] = JsonNode.Parse(
            """[{"payment_form_id": 1, "payment_method_id": 10}, {"payment_form_id": 1, "payment_method_id": 999}]"""));

        var result = await harness.IssueAsync("invoice", input);

        AssertValidation(result, "payment_form.1.payment_method_id", "payment form.1.payment method id es inválido.");
    }

    // Lo que ya existía: un objeto suelto sigue siendo una forma de pago, con los errores sin índice.
    [Test]
    public async Task A_single_payment_form_object_keeps_its_paths_without_index()
    {
        using var harness = IssueHarness.Create();
        var input = IssueHarness.Edited(IssueHarness.FirstInvoice, json => json["payment_form"] = JsonNode.Parse(
            """{"payment_form_id": 1, "payment_method_id": 999}"""));

        var result = await harness.IssueAsync("invoice", input);

        AssertValidation(result, "payment_form.payment_method_id", "payment form.payment method id es inválido.");
    }

    [Test]
    public async Task Without_payment_form_the_php_default_is_used()
    {
        using var harness = IssueHarness.Create();
        var input = IssueHarness.Edited(IssueHarness.FirstInvoice, json => json.Remove("payment_form"));

        var result = await harness.IssueAsync("invoice", input);

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(CountOf(Unsigned(result), "<cac:PaymentMeans>"), Is.EqualTo(1));
        Assert.That(harness.SingleGraph.Payments.Select(payment => (payment.PaymentFormId, payment.PaymentMethodId)), Is.EqualTo(new[] { (1, 10) }));
    }

    private static int CountOf(string text, string value) =>
        (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;

    private static string Unsigned(Result<IssueDocumentResponse> result) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(result.Value.UnsignedInvoiceXml!));

    private static void AssertValidation(Result<IssueDocumentResponse> result, string field, string message)
    {
        Assert.That(result.IsSuccess, Is.False, "Expected a validation error.");
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
        Assert.That(result.Error.Fields, Does.Contain(new FieldError(field, message)), string.Join(" | ", result.Error.Fields));
    }

    private static string Describe(Result<IssueDocumentResponse> result) =>
        result.IsSuccess ? "" : $"{result.Error.Code}: {result.Error.Message} {string.Join(" | ", result.Error.Fields)}";
}
