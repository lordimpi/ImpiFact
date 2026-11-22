using System.Text;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;

namespace ImpiFact.Application.Tests.Features.Documents.Issue;

/// <summary>
/// Health-sector invoice without RIPS (phase 7a, T7; php-rules 17): <c>health_fields</c> on the national invoice, read as
/// the Ministerio de Salud's Documento Técnico 2 v001 (Resolución 948 de 2026) defines the XML. ApiDian Community ignored it.
/// </summary>
public sealed class HealthInvoiceTests
{
    private const string User = """
        {"provider_code":"1100100001","health_type_document_identification_id":1,"identification_number":"A00000001",
         "surname":"PACIENTE","first_name":"PRIMERO","health_type_user_id":1,"health_contracting_payment_method_id":4,
         "health_coverage_id":16,"autorization_numbers":"A12345","contract_number":"CUCON-0001","co_payment":"3300.00",
         "moderating_fee":0,"recovery_fee":0,"shared_payment":0}
        """;

    [Test]
    public async Task A_health_invoice_writes_the_operation_the_period_and_the_extension()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", Health(fields => { }));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        var xml = Unsigned(result);
        Assert.That(xml, Does.Contain("<cbc:CustomizationID>SS-CUFE</cbc:CustomizationID>"));
        Assert.That(xml, Does.Contain(
            "</cbc:LineCountNumeric>\n  <cac:InvoicePeriod>\n    <cbc:StartDate>2026-11-01</cbc:StartDate>\n" +
            "    <cbc:EndDate>2026-11-15</cbc:EndDate>\n  </cac:InvoicePeriod>\n"));
        Assert.That(xml, Does.Contain("<Value>url www.minsalud.gov.co</Value>"));
        Assert.That(xml, Does.Contain("<Name>CODIGO_PRESTADOR</Name>\n                  <Value>1100100001</Value>"));
        Assert.That(xml, Does.Contain("<Value schemeName=\"salud_modalidad_pago.gc\" schemeID=\"04\">Pago por evento</Value>"));
        Assert.That(xml, Does.Contain(
            "<Value schemeName=\"salud_cobertura.gc\" schemeID=\"16\">Plan de beneficios en salud financiado con UPC Contributivo</Value>"));
        Assert.That(xml, Does.Contain("<Name>NUMERO_CONTRATO</Name>\n                  <Value>CUCON-0001</Value>"));
        Assert.That(xml, Does.Contain("<Name>NUMERO_POLIZA</Name>\n                  <Value/>"));
        var extensions = harness.SingleGraph.Document.Extensions;
        Assert.That(extensions?["variant"]?.GetValue<string>(), Is.EqualTo("health"));
        Assert.That(extensions?["health_fields"]?["health_type_operation_id"]?.GetValue<int>(), Is.EqualTo(1));
        Assert.That(extensions?["health_fields"]?["users_info"]?.AsArray(), Has.Count.EqualTo(1));
    }

    // Documento Técnico 2 v001, section 6.a: «La extensión donde se incluyen las variables del Sector Salud no se debe
    // informar para las operaciones SS-Recaudo».
    [Test]
    public async Task A_collection_invoice_has_the_period_but_no_extension()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", Health(fields =>
        {
            fields["health_type_operation_id"] = 5;
            fields["users_info"] = new JsonArray();
        }));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        var xml = Unsigned(result);
        Assert.That(xml, Does.Contain("<cbc:CustomizationID>SS-Recaudo</cbc:CustomizationID>"));
        Assert.That(xml, Does.Contain("<cac:InvoicePeriod>"));
        Assert.That(xml, Does.Not.Contain("CustomTagGeneral"));
    }

    [Test]
    public async Task A_collection_invoice_with_users_is_rejected()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", Health(fields => fields["health_type_operation_id"] = 5));

        AssertValidation(result, "health_fields.users_info",
            "health fields.users info debe estar vacío cuando health_type_operation_id es 5 (SS-Recaudo): esa operación no lleva la " +
            "extensión del sector salud.");
    }

    // Section 8.a: SS-CUFE, SS-CUDE, SS-POS and SS-SNum (and SS-Reporte, 8.c) must carry the health extension.
    [TestCase(1)]
    [TestCase(6)]
    public async Task An_operation_with_the_extension_needs_a_user(int operation)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", Health(fields =>
        {
            fields["health_type_operation_id"] = operation;
            fields["users_info"] = new JsonArray();
        }));

        AssertValidation(result, "health_fields.users_info",
            $"El campo health fields.users info es obligatorio cuando health_type_operation_id es {operation}: esa operación lleva " +
            "la extensión del sector salud.");
    }

    // Section 8, «MODOS DE USO - 4: SS-SINAPORTE»: the extension goes only when there is something to report.
    [Test]
    public async Task A_no_contribution_invoice_may_omit_the_users()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", Health(fields =>
        {
            fields["health_type_operation_id"] = 7;
            fields["users_info"] = new JsonArray();
        }));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(Unsigned(result), Does.Contain("<cbc:CustomizationID>SS-SinAporte</cbc:CustomizationID>"));
        Assert.That(Unsigned(result), Does.Not.Contain("CustomTagGeneral"));
    }

    // Sections 4.10 and 4.11: both dates are required, as Y-m-d.
    [Test]
    public async Task The_billing_period_is_required()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", Health(fields =>
        {
            fields.Remove("invoice_period_start_date");
            fields["invoice_period_end_date"] = "15/11/2026";
        }));

        AssertValidation(result, "health_fields.invoice_period_start_date", "El campo health fields.invoice period start date es obligatorio.");
        AssertValidation(result, "health_fields.invoice_period_end_date", "health fields.invoice period end date no corresponde al formato Y-m-d.");
    }

    [Test]
    public async Task The_period_cannot_end_before_it_starts()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", Health(fields => fields["invoice_period_end_date"] = "2026-10-31"));

        AssertValidation(result, "health_fields.invoice_period_end_date",
            "health fields.invoice period end date debe ser una fecha posterior o igual a health fields.invoice period start date.");
    }

    [TestCase(null, "El campo health fields.health type operation id es obligatorio.")]
    [TestCase(8, "health fields.health type operation id es inválido. Use un tipo de operación del sector salud: 1 (SS-CUFE), 2 (SS-CUDE), " +
                 "3 (SS-POS), 4 (SS-SNum), 5 (SS-Recaudo), 6 (SS-Reporte) o 7 (SS-SinAporte).")]
    public async Task The_health_operation_must_exist(int? operation, string message)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", Health(fields => fields["health_type_operation_id"] = operation));

        AssertValidation(result, "health_fields.health_type_operation_id", message);
    }

    // Sections 4.1 to 4.3: provider code, payment method and coverage are required, from the SISPRO tables.
    [Test]
    public async Task A_user_needs_the_provider_payment_method_and_coverage()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", Health(fields => fields["users_info"] = JsonNode.Parse("""[{"identification_number":"1"}]""")));

        AssertValidation(result, "health_fields.users_info.0.provider_code", "El campo health fields.users info.0.provider code es obligatorio.");
        AssertValidation(result, "health_fields.users_info.0.health_contracting_payment_method_id",
            "El campo health fields.users info.0.health contracting payment method id es obligatorio.");
        AssertValidation(result, "health_fields.users_info.0.health_coverage_id",
            "El campo health fields.users info.0.health coverage id es obligatorio.");
    }

    [TestCase("health_contracting_payment_method_id", 5,
        "health fields.users info.0.health contracting payment method id es inválido. Use un id de la tabla modalidadPago de SISPRO, de 1 a 4.")]
    [TestCase("health_coverage_id", 18,
        "health fields.users info.0.health coverage id es inválido. Use un id de la tabla coberturaPlan de SISPRO, de 1 a 17.")]
    [TestCase("health_invoice_without_contract_id", 8,
        "health fields.users info.0.health invoice without contract id es inválido. Use un id de la tabla facturaSinContrato de SISPRO, de 1 a 7.")]
    [TestCase("health_type_document_identification_id", 14,
        "health fields.users info.0.health type document identification id es inválido. Use un id de la tabla de tipos de documento " +
        "del sector salud, de 1 a 13.")]
    public async Task A_user_code_must_be_in_its_table(string field, int id, string message)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", Health(fields => UserOf(fields)[field] = id));

        AssertValidation(result, $"health_fields.users_info.0.{field}", message);
    }

    // Section 6.a: «Cuando sea informado un numero de contrato no se podrá informar un número de póliza».
    [Test]
    public async Task A_contract_and_a_policy_are_not_sent_together()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", Health(fields => UserOf(fields)["policy_number"] = "POL-1"));

        AssertValidation(result, "health_fields.users_info.0.policy_number",
            "health fields.users info.0.policy number no se puede enviar junto con contract_number: la factura del sector salud lleva " +
            "el número de contrato o el de póliza, no los dos.");
    }

    // Section 4.12 and 6.a: the justification of an invoice without contract replaces the contract number.
    [Test]
    public async Task An_invoice_without_contract_writes_its_justification()
    {
        using var harness = IssueHarness.Create();

        var withContract = await harness.IssueAsync("invoice", Health(fields => UserOf(fields)["health_invoice_without_contract_id"] = 1));
        var withoutContract = await IssueHarness.Create().IssueAsync("invoice", Health(fields =>
        {
            UserOf(fields)["contract_number"] = "";
            UserOf(fields)["health_invoice_without_contract_id"] = 1;
        }));

        AssertValidation(withContract, "health_fields.users_info.0.health_invoice_without_contract_id",
            "health fields.users info.0.health invoice without contract id solo se envía cuando no hay contract_number.");
        Assert.That(withoutContract.IsSuccess, Is.True, Describe(withoutContract));
        Assert.That(Unsigned(withoutContract), Does.Contain(
            "<Name>FACTURA_SIN_CONTRATO</Name>\n                  <Value schemeName=\"salud_facturaSinContrato.gc\" schemeID=\"01\">ATENCION DE URGENCIAS</Value>"));
    }

    // Sections 4.2, 4.3 and 6.a: one Collection per invoice, so every user shares those values.
    [Test]
    public async Task Users_that_disagree_on_the_collection_values_are_rejected()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", Health(fields =>
        {
            var second = JsonNode.Parse(User)!.AsObject();
            second["health_coverage_id"] = 17;
            fields["users_info"]!.AsArray().Add(second);
        }));

        AssertValidation(result, "health_fields.users_info.1.health_coverage_id",
            "health fields.users info.1.health coverage id debe ser igual a health_fields.users_info.0.health_coverage_id: la factura del " +
            "sector salud lleva una sola colección de datos (Documento Técnico 2, sección 6), así que todos los usuarios comparten " +
            "prestador, modalidad de pago, cobertura, contrato, póliza y justificación sin contrato.");
    }

    // Sections 4.6 to 4.9: «Se valida que no existan valores negativos».
    [Test]
    public async Task A_negative_user_amount_is_rejected()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", Health(fields => UserOf(fields)["co_payment"] = "-1"));

        AssertValidation(result, "health_fields.users_info.0.co_payment", "health fields.users info.0.co payment no puede ser negativo.");
    }

    // Section 7.c: every prepaid payment of a health invoice carries its collection concept (conceptoRecaudo) as schemeID.
    [Test]
    public async Task A_health_prepaid_payment_writes_its_concept()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", Health(fields => { }, json =>
        {
            json["prepaid_payments"] = JsonNode.Parse(
                """[{"idpayment":"1","prepaid_payment_type_id":1,"paidamount":"3300.00","receiveddate":"2026-11-14"}]""");
            json["legal_monetary_totals"]!["pre_paid_amount"] = "3300.00";
        }));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(Unsigned(result), Does.Contain("<cac:PrepaidPayment>\n    <cbc:ID schemeID=\"01\">1</cbc:ID>"));
    }

    // Validation rule DND110 of the Ministry («[PrepaidPayment.ID.schemeID] El apartado no existe») and NCV «El apartado debe
    // ser informado una única vez por concepto».
    [Test]
    public async Task A_health_prepaid_payment_needs_one_known_concept_each()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", Health(fields => { }, json =>
        {
            json["prepaid_payments"] = JsonNode.Parse(
                """
                [{"idpayment":"1","paidamount":"1.00","receiveddate":"2026-11-14"},
                 {"idpayment":"2","prepaid_payment_type_id":2,"paidamount":"1.00","receiveddate":"2026-11-14"},
                 {"idpayment":"3","prepaid_payment_type_id":2,"paidamount":"1.00","receiveddate":"2026-11-14"},
                 {"idpayment":"4","prepaid_payment_type_id":9,"paidamount":"1.00","receiveddate":"2026-11-14"}]
                """);
            json["legal_monetary_totals"]!["pre_paid_amount"] = "4.00";
        }));

        AssertValidation(result, "prepaid_payments.0.prepaid_payment_type_id",
            "El campo prepaid payments.0.prepaid payment type id es obligatorio cuando health fields está presente.");
        AssertValidation(result, "prepaid_payments.2.prepaid_payment_type_id",
            "prepaid payments.2.prepaid payment type id repite un concepto: la factura del sector salud lleva un anticipo por concepto.");
    }

    [Test]
    public async Task An_unknown_prepaid_concept_is_rejected()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", Health(fields => { }, json =>
        {
            json["prepaid_payment"] = JsonNode.Parse("""{"idpayment":"1","prepaid_payment_type_id":9,"paidamount":"1.00","receiveddate":"2026-11-14"}""");
            json["legal_monetary_totals"]!["pre_paid_amount"] = "1.00";
        }));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error.Fields.Select(field => field.Field), Does.Contain("prepaid_payment.prepaid_payment_type_id"), Describe(result));
    }

    // php-rules 3.2: a plain invoice keeps cbc:ID without attributes even if the client sends a concept (ApiDian writes the
    // schemeID only with health fields, _prepaid_payment.blade.php).
    [Test]
    public async Task A_plain_invoice_ignores_the_prepaid_concept()
    {
        using var harness = IssueHarness.Create();
        var input = IssueHarness.Edited(IssueHarness.FirstInvoice, json =>
        {
            json["prepaid_payment"] = JsonNode.Parse("""{"idpayment":"1","prepaid_payment_type_id":1,"paidamount":"1.00","receiveddate":"2026-11-14"}""");
            json["legal_monetary_totals"]!["pre_paid_amount"] = "1.00";
        });

        var result = await harness.IssueAsync("invoice", input);

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(Unsigned(result), Does.Contain("<cac:PrepaidPayment>\n    <cbc:ID>1</cbc:ID>"));
        Assert.That(Unsigned(result), Does.Contain("<cbc:CustomizationID>10</cbc:CustomizationID>"));
    }

    // The other invoice routes and the notes keep ignoring health_fields, as ApiDian did.
    [TestCase("invoice-aiu")]
    [TestCase("credit-note")]
    public async Task Other_documents_ignore_the_health_fields(string kind)
    {
        using var harness = IssueHarness.Create();
        var caseDir = kind == "credit-note" ? IssueHarness.FirstCreditNote : GoldenFiles.Cases("invoice-aiu").First();
        var plain = await IssueHarness.Create().IssueAsync(kind, IssueHarness.GoldenInput(caseDir));

        var result = await harness.IssueAsync(kind, IssueHarness.Edited(caseDir, json => json["health_fields"] = HealthFields()));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(Unsigned(result), Is.EqualTo(Unsigned(plain)));
    }

    private static JsonObject HealthFields() => JsonNode.Parse(
        "{\"invoice_period_start_date\":\"2026-11-01\",\"invoice_period_end_date\":\"2026-11-15\",\"health_type_operation_id\":1," +
        "\"print_users_info_to_pdf\":true,\"users_info\":[" + User + "]}")!.AsObject();

    private static byte[] Health(Action<JsonObject> change, Action<JsonObject>? body = null) =>
        IssueHarness.Edited(IssueHarness.FirstInvoice, json =>
        {
            var fields = HealthFields();
            change(fields);
            json["health_fields"] = fields;
            body?.Invoke(json);
        });

    private static JsonObject UserOf(JsonObject fields) => fields["users_info"]![0]!.AsObject();

    private static string Unsigned(Result<IssueDocumentResponse> result) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(result.Value.UnsignedInvoiceXml!));

    private static void AssertValidation(Result<IssueDocumentResponse> result, string field, string message)
    {
        Assert.That(result.IsSuccess, Is.False, "Expected a validation error.");
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
        Assert.That(result.Error.Fields, Does.Contain(new FieldError(field, message)), string.Join(" | ", result.Error.Fields));
    }

    private static string Describe(Result<IssueDocumentResponse> result) =>
        result.IsSuccess ? "ok" : string.Join(" | ", result.Error.Fields.Select(field => $"{field.Field}: {field.Message}"));
}
