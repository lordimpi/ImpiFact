using System.Text;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Domain.Common;

namespace ImpiFact.Application.Tests.Features.Documents.Issue;

/// <summary>
/// AIU, mandate and freight transport invoices through the use case (phase 7a, T2; php-rules, sections 9 and 10): the
/// golden XML and CUFE, the fixed operation of each variant, their own request rules with the Laravel messages, and
/// that they are type 1 invoices for the numbering and the plan.
/// </summary>
public sealed class InvoiceVariantTests
{
    private static readonly (string Kind, int Operation, string Variant)[] Variants =
    [
        ("invoice-aiu", 9, "aiu"),
        ("invoice-mandate", 11, "mandate"),
        ("invoice-transport", 21, "transport"),
    ];

    public static IEnumerable<TestCaseData> GoldenCases() =>
        Variants.SelectMany(variant => GoldenFiles.Cases(variant.Kind).Select(dir =>
            new TestCaseData(variant.Kind, variant.Operation, variant.Variant, dir).SetArgDisplayNames(variant.Kind + "/" + Path.GetFileName(dir))));

    [TestCaseSource(nameof(GoldenCases))]
    public async Task Golden_input_issues_the_php_xml_and_cufe_as_a_type_1_invoice(string kind, int operation, string variant, string caseDir)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(kind, IssueHarness.GoldenInput(caseDir));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(result.Value.Cufe, Is.EqualTo(File.ReadAllText(Path.Combine(caseDir, "expected.cufe")).Trim()));
        Assert.That(result.Value.Success, Is.True);
        Assert.That(Unsigned(result), Is.EqualTo(ExpectedUnsigned(caseDir)));
        var document = harness.SingleGraph.Document;
        Assert.That((document.DocumentTypeId, document.TypeOperationId, document.UuidAlgorithm), Is.EqualTo((1, operation, "CUFE-SHA384")));
        Assert.That(document.Extensions?["variant"]?.GetValue<string>(), Is.EqualTo(variant));
        Assert.That(harness.Plans.Checks, Is.EqualTo(new[] { (harness.CompanyId, DocumentKind.Invoice) }));
    }

    // The variants share the invoice resolution and sequence: an AIU invoice and a standard one take consecutive numbers.
    [Test]
    public async Task A_variant_takes_the_next_number_of_the_invoice_sequence()
    {
        using var harness = IssueHarness.Create();
        var standard = IssueHarness.Edited(IssueHarness.FirstInvoice, json => json.Remove("number"));
        var aiu = IssueHarness.Edited(Golden("invoice-aiu"), json => json.Remove("number"));

        var first = await harness.IssueAsync("invoice", standard);
        var second = await harness.IssueAsync("invoice-aiu", aiu);

        Assert.That(first.IsSuccess && second.IsSuccess, Is.True, Describe(first) + Describe(second));
        Assert.That(second.Value.Document.Number, Is.EqualTo(first.Value.Document.Number + 1));
        Assert.That(second.Value.Document.DocumentTypeId, Is.EqualTo(1));
    }

    [TestCase("invoice-aiu", 9)]
    [TestCase("invoice-mandate", 11)]
    [TestCase("invoice-transport", 21)]
    public async Task The_variant_operation_may_be_sent_and_any_other_is_rejected(string kind, int operation)
    {
        using var harness = IssueHarness.Create();
        var same = IssueHarness.Edited(Golden(kind), json => json["type_operation_id"] = operation);
        var other = IssueHarness.Edited(Golden(kind), json => json["type_operation_id"] = 10);

        var accepted = await harness.IssueAsync(kind, same);
        var rejected = await harness.IssueAsync(kind, other);

        Assert.That(accepted.IsSuccess, Is.True, Describe(accepted));
        AssertValidation(rejected, "type_operation_id", "type operation id es inválido.");
    }

    // Previous behaviour of the standard invoice: any operation is accepted and the variant fields are ignored.
    [Test]
    public async Task A_standard_invoice_keeps_ignoring_the_variant_fields()
    {
        using var harness = IssueHarness.Create();
        var input = IssueHarness.Edited(Golden("invoice-aiu"), json => json["type_operation_id"] = 9);

        var result = await harness.IssueAsync("invoice", input);

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        var xml = Unsigned(result);
        Assert.That(xml, Does.Contain("<cbc:CustomizationID>09</cbc:CustomizationID>"));
        Assert.That(xml, Does.Not.Contain("Contrato de servicios AIU"));
        Assert.That(harness.SingleGraph.Document.Extensions, Is.Null);
    }

    [Test]
    public async Task An_aiu_invoice_requires_the_contract_object()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice-aiu", IssueHarness.Edited(Golden("invoice-aiu"), json => json.Remove("noteAIU")));

        AssertValidation(result, "noteAIU", "El campo noteAIU es obligatorio.");
        Assert.That(harness.Db.Graphs, Is.Empty);
    }

    [Test]
    public async Task An_aiu_invoice_stores_the_contract_object()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice-aiu", IssueHarness.GoldenInput(Golden("invoice-aiu")));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(harness.SingleGraph.Document.Extensions?["note_aiu"]?.GetValue<string>(), Is.EqualTo("OBRA DE PRUEBA GOLDEN"));
    }

    [TestCase("12345678A", null, "invoice_lines.2.agentparty", "invoice lines.2.agentparty debe tener entre 1 y 15 dígitos.")]
    [TestCase("1234567890123456", "1", "invoice_lines.2.agentparty", "invoice lines.2.agentparty debe tener entre 1 y 15 dígitos.")]
    [TestCase("800000005", null, "invoice_lines.2.agentparty_dv",
        "El campo invoice lines.2.agentparty dv es obligatorio cuando invoice lines.2.agentparty está presente.")]
    [TestCase("800000005", "12", "invoice_lines.2.agentparty_dv", "invoice lines.2.agentparty dv debe tener 1 dígitos.")]
    [TestCase("800000005", "3", "invoice_lines.2.agentparty_dv", "Error, el digito de verificacion no es valido para este NIT.")]
    public async Task The_mandator_must_be_a_nit_with_its_check_digit(string nit, string? dv, string field, string message)
    {
        using var harness = IssueHarness.Create();
        var input = IssueHarness.Edited(Golden("invoice-mandate"), json =>
        {
            var line = json["invoice_lines"]![2]!.AsObject();
            line["agentparty"] = nit;
            line.Remove("agentparty_dv");
            if (dv is not null)
            {
                line["agentparty_dv"] = dv;
            }
        });

        AssertValidation(await harness.IssueAsync("invoice-mandate", input), field, message);
    }

    [Test]
    public async Task The_mandator_is_optional_and_stored_with_its_line()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice-mandate", IssueHarness.GoldenInput(Golden("invoice-mandate")));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        var lines = harness.SingleGraph.Lines;
        Assert.That(lines.Take(2).Select(line => line.Extensions), Is.All.Null);
        Assert.That(lines[2].Extensions?.ToJsonString(), Is.EqualTo("""{"agentparty":"800000005","agentparty_dv":"9"}"""));
    }

    [Test]
    public async Task A_transport_line_requires_is_rndc()
    {
        using var harness = IssueHarness.Create();
        var input = IssueHarness.Edited(Golden("invoice-transport"), json => json["invoice_lines"]![1]!.AsObject().Remove("is_RNDC"));

        AssertValidation(await harness.IssueAsync("invoice-transport", input), "invoice_lines.1.is_RNDC", "El campo invoice lines.1.is RNDC es obligatorio.");
    }

    [TestCase("RNDC_consignment_number")]
    [TestCase("internal_consignment_number")]
    [TestCase("value_consignment")]
    [TestCase("unit_measure_consignment_id")]
    [TestCase("quantity_consignment")]
    public async Task An_rndc_line_requires_each_consignment_field(string field)
    {
        using var harness = IssueHarness.Create();
        var input = IssueHarness.Edited(Golden("invoice-transport"), json => json["invoice_lines"]![0]!.AsObject().Remove(field));

        var name = field.Replace('_', ' ');
        AssertValidation(await harness.IssueAsync("invoice-transport", input), $"invoice_lines.0.{field}",
            $"El campo invoice lines.0.{name} es obligatorio cuando invoice lines.0.is RNDC es true.");
    }

    [TestCase("RNDC_consignment_number", "95-0009", "invoice lines.0.RNDC consignment number debe ser numérico.")]
    [TestCase("value_consignment", "600000.50", "invoice lines.0.value consignment debe ser un número entero.")]
    [TestCase("quantity_consignment", "doscientos", "invoice lines.0.quantity consignment debe ser numérico.")]
    [TestCase("unit_measure_consignment_id", "99999", "invoice lines.0.unit measure consignment id es inválido.")]
    public async Task Consignment_values_must_have_their_type(string field, string value, string message)
    {
        using var harness = IssueHarness.Create();
        var input = IssueHarness.Edited(Golden("invoice-transport"), json => json["invoice_lines"]![0]![field] = value);

        AssertValidation(await harness.IssueAsync("invoice-transport", input), $"invoice_lines.0.{field}", message);
    }

    [Test]
    public async Task The_consignment_is_stored_with_its_line()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice-transport", IssueHarness.GoldenInput(Golden("invoice-transport")));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        var lines = harness.SingleGraph.Lines;
        Assert.That(lines[0].Extensions?.ToJsonString(), Is.EqualTo(string.Concat(
            """{"is_RNDC":true,"RNDC_consignment_number":"95000912001","internal_consignment_number":"REM000001",""",
            "\"value_consignment\":\"600000\",\"unit_measure_consignment_id\":767,\"quantity_consignment\":\"200.00\"}")));
        Assert.That(lines[1].Extensions?.ToJsonString(), Is.EqualTo("""{"is_RNDC":false}"""));
    }

    private static string Golden(string kind) => GoldenFiles.Cases(kind).First();

    private static string Unsigned(Result<IssueDocumentResponse> result) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(result.Value.UnsignedInvoiceXml!));

    private static string ExpectedUnsigned(string caseDir) =>
        Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(caseDir, "expected.xml")))
            .Replace("<ext:ExtensionContent></ext:ExtensionContent>", "<ext:ExtensionContent/>", StringComparison.Ordinal);

    private static void AssertValidation(Result<IssueDocumentResponse> result, string field, string message)
    {
        Assert.That(result.IsSuccess, Is.False, "Expected a validation error.");
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
        Assert.That(result.Error.Fields, Does.Contain(new FieldError(field, message)), string.Join(" | ", result.Error.Fields));
    }

    private static string Describe(Result<IssueDocumentResponse> result) =>
        result.IsSuccess ? "" : $"{result.Error.Code}: {result.Error.Message} {string.Join(" | ", result.Error.Fields)}";
}
