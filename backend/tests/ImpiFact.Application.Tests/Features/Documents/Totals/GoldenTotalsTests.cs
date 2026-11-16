using System.Text.Json;
using ImpiFact.Application.Features.Documents.Totals;
using ImpiFact.Dian.Rounding;

namespace ImpiFact.Application.Tests.Features.Documents.Totals;

/// <summary>
/// Los documentos de referencia ("golden") que generó ApiDian (el PHP original) pasan la validación de
/// totales: el validador no rechaza solicitudes reales que el PHP aceptaba. El mapeo de la solicitud al
/// modelo es mínimo y solo vive en esta prueba; el de producción lo hacen los casos de uso.
/// </summary>
public sealed class GoldenTotalsTests
{
    private static IEnumerable<TestCaseData> GoldenInputs() =>
        Directory.EnumerateFiles(FindGoldenRoot(), "input.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => new TestCaseData(path).SetArgDisplayNames(
                Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)))
                + "/" + Path.GetFileName(Path.GetDirectoryName(path))));

    [Test]
    public void All_golden_inputs_are_found()
    {
        Assert.That(GoldenInputs().Count(), Is.EqualTo(14));
    }

    [TestCaseSource(nameof(GoldenInputs))]
    public void Golden_input_passes(string path)
    {
        var document = Map(path);

        var result = DianTotalsValidator.Validate(document);

        Assert.That(result.IsSuccess, Is.True, TotalsSamples.Describe(result));
    }

    private static TotalsDocument Map(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var root = json.RootElement;
        var kind = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))) switch
        {
            "invoice" => TotalsDocumentKind.Invoice,
            "credit-note" => TotalsDocumentKind.CreditNote,
            "debit-note" => TotalsDocumentKind.DebitNote,
            var other => throw new InvalidOperationException("Unknown golden document type " + other),
        };
        var totals = root.GetProperty(kind == TotalsDocumentKind.DebitNote ? "requested_monetary_totals" : "legal_monetary_totals");
        var lines = root.GetProperty(kind switch
        {
            TotalsDocumentKind.Invoice => "invoice_lines",
            TotalsDocumentKind.CreditNote => "credit_note_lines",
            _ => "debit_note_lines",
        });

        return new TotalsDocument
        {
            Kind = kind,
            MonetaryTotals = new MonetaryTotalsInput
            {
                LineExtensionAmount = Number(totals, "line_extension_amount")!.Value,
                TaxExclusiveAmount = Number(totals, "tax_exclusive_amount")!.Value,
                TaxInclusiveAmount = Number(totals, "tax_inclusive_amount")!.Value,
                // El PHP solo escribe estos totales si son distintos de cero (php-rules, sección 3.2).
                AllowanceTotalAmount = NonZero(Number(totals, "allowance_total_amount")),
                ChargeTotalAmount = NonZero(Number(totals, "charge_total_amount")),
                PrepaidAmount = NonZero(Number(totals, "pre_paid_amount")),
                PayableAmount = Number(totals, "payable_amount")!.Value,
            },
            TaxTotals = Taxes(root),
            AllowanceCharges = AllowanceCharges(root),
            PrepaidPayments = Items(root, "prepaid_payments")
                .Select(p => new PrepaidPaymentInput(Number(p, "paidamount") ?? 0m))
                .ToList(),
            Lines = lines.EnumerateArray().Select(line => new TotalsLineInput
            {
                Quantity = Number(line, "invoiced_quantity")!.Value,
                LineExtensionAmount = Number(line, "line_extension_amount")!.Value,
                PriceAmount = Number(line, "price_amount")!.Value,
                BaseQuantity = Number(line, "base_quantity")!.Value,
                AllowanceCharges = AllowanceCharges(line),
                TaxTotals = Taxes(line),
            }).ToList(),
        };
    }

    private static List<TaxSubtotalInput> Taxes(JsonElement parent) =>
        Items(parent, "tax_totals").Select(tax => new TaxSubtotalInput
        {
            TaxId = tax.GetProperty("tax_id").GetInt32(),
            TaxableAmount = Number(tax, "taxable_amount") ?? 0m,
            TaxAmount = Number(tax, "tax_amount")!.Value,
            Percent = Number(tax, "percent"),
            PerUnitAmount = Number(tax, "per_unit_amount"),
            BaseUnitMeasure = Number(tax, "base_unit_measure"),
        }).ToList();

    private static List<AllowanceChargeInput> AllowanceCharges(JsonElement parent) =>
        Items(parent, "allowance_charges").Select(item => new AllowanceChargeInput(
            item.GetProperty("charge_indicator") is { ValueKind: JsonValueKind.True or JsonValueKind.False } flag
                ? flag.GetBoolean()
                : string.Equals(item.GetProperty("charge_indicator").GetString(), "true", StringComparison.OrdinalIgnoreCase),
            Number(item, "amount")!.Value)).ToList();

    private static IEnumerable<JsonElement> Items(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray() : [];

    private static decimal? Number(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? DianRounding.Parse(value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText())
            : null;

    private static decimal? NonZero(decimal? value) => value is 0m ? null : value;

    private static string FindGoldenRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "golden");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("tests/golden not found above " + AppContext.BaseDirectory);
    }
}
