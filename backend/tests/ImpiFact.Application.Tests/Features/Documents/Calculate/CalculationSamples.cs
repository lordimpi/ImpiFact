using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Calculate;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Application.Features.Documents.Totals;
using ImpiFact.Dian.Rounding;

namespace ImpiFact.Application.Tests.Features.Documents.Calculate;

/// <summary>
/// Piezas de las pruebas del cálculo: leen los casos golden (solicitudes reales que generó ApiDian, el PHP original)
/// como borradores del cálculo y llevan un resultado del cálculo a la emisión, con el mismo camino que usa el caso de
/// uso de emisión: el cuerpo heredado (JSON), <see cref="IssueDocumentJson"/>, <see cref="LegacyDocumentMapper"/> y
/// <see cref="DianTotalsValidator"/>.
/// </summary>
internal static class CalculationSamples
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Solo facturas y notas: los casos de attached-document no traen líneas que calcular.</summary>
    private static readonly string[] DocumentTypes = ["invoice", "credit-note", "debit-note"];

    public static IEnumerable<TestCaseData> GoldenCases() =>
        DocumentTypes
            .SelectMany(type => Directory.EnumerateDirectories(Path.Combine(FindGoldenRoot(), type)))
            .Order(StringComparer.Ordinal)
            .Select(dir => new TestCaseData(Name(dir)).SetArgDisplayNames(Name(dir)));

    /// <summary>El <c>input.json</c> de un caso golden (por ejemplo <c>invoice/single-line-iva19</c>).</summary>
    public static JsonObject GoldenInput(string name) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(FindGoldenRoot(), name, "input.json")))!.AsObject();

    public static IssueSpec SpecOf(string name) => name.Split('/')[0] switch
    {
        "invoice" => IssueSpec.Invoice,
        "credit-note" => IssueSpec.CreditNote,
        "debit-note" => IssueSpec.DebitNote,
        var other => throw new InvalidOperationException("Unknown golden document type " + other),
    };

    /// <summary>
    /// El borrador del cálculo que corresponde a las líneas de una solicitud heredada: cantidad, precio unitario, línea
    /// gratuita, descuentos y cargos por su valor e impuestos por su tarifa o su valor por unidad.
    /// </summary>
    public static CalculateDocumentTotalsRequest RequestFrom(JsonObject legacy, IssueSpec spec) => new()
    {
        Lines = legacy[spec.LinesField]!.AsArray().Select(node =>
        {
            var line = node!.AsObject();
            Assert.That(Number(line, "base_quantity"), Is.EqualTo(Number(line, "invoiced_quantity")),
                "The golden price is the price of one unit only when base_quantity equals invoiced_quantity.");
            return new CalculationLineRequest
            {
                Quantity = Number(line, "invoiced_quantity"),
                UnitPrice = Number(line, "price_amount"),
                FreeOfCharge = line["free_of_charge_indicator"]?.GetValue<bool>() ?? false,
                AllowanceCharges = Items(line, "allowance_charges").Select(AllowanceCharge).ToList(),
                Taxes = Items(line, "tax_totals").Select(tax => new CalculationTaxRequest
                {
                    TaxId = (int)Number(tax, "tax_id")!.Value,
                    Percent = Number(tax, "per_unit_amount") is null ? Number(tax, "percent") : null,
                    PerUnitAmount = Number(tax, "per_unit_amount"),
                    BaseUnitMeasure = Number(tax, "base_unit_measure"),
                    UnitMeasureId = Number(tax, "unit_measure_id") is { } unit ? (int)unit : null,
                }).ToList(),
            };
        }).ToList(),
        AllowanceCharges = Items(legacy, "allowance_charges").Select(AllowanceCharge).ToList(),
        PrepaidPayments = Items(legacy, "prepaid_payments")
            .Select(payment => new CalculationPrepaidPaymentRequest { PaidAmount = Number(payment, "paidamount") })
            .ToList(),
    };

    /// <summary>
    /// Los valores de una solicitud heredada como quedan escritos en el XML (cada importe con 2 decimales), con una
    /// ruta por valor, para compararlos con <see cref="Describe(DocumentTotalsCalculation)"/>.
    /// </summary>
    public static List<string> Describe(JsonObject legacy, IssueSpec spec)
    {
        var values = new List<string>();
        var lines = legacy[spec.LinesField]!.AsArray();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i]!.AsObject();
            values.Add($"lines[{i}].line_extension_amount={Amount(Number(line, "line_extension_amount"))}");
            values.AddRange(Items(line, "allowance_charges").SelectMany((item, j) => DescribeCharge($"lines[{i}].allowance_charges[{j}]", item)));
            values.AddRange(Items(line, "tax_totals").SelectMany((tax, j) => DescribeTax($"lines[{i}].tax_subtotals[{j}]", tax)));
        }

        values.AddRange(Items(legacy, "tax_totals").SelectMany((tax, j) => DescribeTax($"tax_subtotals[{j}]", tax)));
        values.AddRange(Items(legacy, "allowance_charges").SelectMany((item, j) => DescribeCharge($"allowance_charges[{j}]", item)));
        var totals = legacy[spec.TotalsField]!.AsObject();
        foreach (var field in TotalsFields)
        {
            values.Add($"totals.{field}={Amount(Number(totals, field) ?? 0m)}");
        }

        return values;
    }

    /// <summary>El resultado del cálculo con las mismas rutas de <see cref="Describe(JsonObject, IssueSpec)"/>.</summary>
    public static List<string> Describe(DocumentTotalsCalculation calculation)
    {
        var values = new List<string>();
        for (var i = 0; i < calculation.Lines.Count; i++)
        {
            var line = calculation.Lines[i];
            values.Add($"lines[{i}].line_extension_amount={Amount(line.LineExtensionAmount)}");
            values.AddRange(line.AllowanceCharges.SelectMany((item, j) => DescribeCharge($"lines[{i}].allowance_charges[{j}]", item)));
            values.AddRange(line.TaxSubtotals.SelectMany((tax, j) => DescribeTax($"lines[{i}].tax_subtotals[{j}]", tax)));
        }

        values.AddRange(calculation.TaxTotals.SelectMany(group => group.TaxSubtotals)
            .SelectMany((tax, j) => DescribeTax($"tax_subtotals[{j}]", tax)));
        values.AddRange(calculation.AllowanceCharges.SelectMany((item, j) => DescribeCharge($"allowance_charges[{j}]", item)));
        var totals = calculation.LegalMonetaryTotals;
        decimal[] amounts =
        [
            totals.LineExtensionAmount, totals.TaxExclusiveAmount, totals.TaxInclusiveAmount, totals.AllowanceTotalAmount,
            totals.ChargeTotalAmount, totals.PayableAmount,
        ];
        values.AddRange(TotalsFields.Zip(amounts, (field, amount) => $"totals.{field}={Amount(amount)}"));
        return values;
    }

    /// <summary>
    /// Arma el cuerpo heredado de la emisión con las líneas del borrador y los valores calculados, como lo hará el panel,
    /// y lo pasa por el mismo camino del caso de uso de emisión hasta <see cref="DianTotalsValidator"/>.
    /// </summary>
    public static Result<TotalsDocument> ValidateAsIssued(
        CalculateDocumentTotalsRequest request, DocumentTotalsCalculation calculation, IssueSpec spec)
    {
        var body = LegacyBody(request, calculation, spec);
        Assert.That(IssueDocumentJson.TryParse(Encoding.UTF8.GetBytes(body.ToJsonString()), out var parsed), Is.True);
        return DianTotalsValidator.Validate(LegacyDocumentMapper.MapTotalsDocument(spec, parsed!));
    }

    /// <summary>
    /// Como <see cref="ValidateAsIssued"/>, pero con los anticipos del borrador. La emisión todavía no lee la lista de
    /// anticipos de la solicitud (queda para la tarea de anticipos de la fase 5), así que hoy rechaza cualquier
    /// <c>pre_paid_amount</c> distinto de cero con la regla AU12; aquí se agregan al documento que revisa el validador
    /// para probar que el total de anticipos calculado cumple esa regla.
    /// </summary>
    public static Result<TotalsDocument> ValidateWithPrepaid(
        CalculateDocumentTotalsRequest request, DocumentTotalsCalculation calculation, IssueSpec spec)
    {
        var body = LegacyBody(request, calculation, spec);
        Assert.That(IssueDocumentJson.TryParse(Encoding.UTF8.GetBytes(body.ToJsonString()), out var parsed), Is.True);
        var document = LegacyDocumentMapper.MapTotalsDocument(spec, parsed!) with
        {
            PrepaidPayments = (request.PrepaidPayments ?? []).Select(payment => new PrepaidPaymentInput(payment.PaidAmount!.Value)).ToList(),
        };
        return DianTotalsValidator.Validate(document);
    }

    /// <summary>El cuerpo heredado (contrato de ApiDian) con los valores calculados, todos como texto con su formato del XML.</summary>
    public static JsonObject LegacyBody(CalculateDocumentTotalsRequest request, DocumentTotalsCalculation calculation, IssueSpec spec)
    {
        var lines = new JsonArray();
        for (var i = 0; i < calculation.Lines.Count; i++)
        {
            var draft = request.Lines![i];
            var line = calculation.Lines[i];
            lines.Add(new JsonObject
            {
                ["invoiced_quantity"] = DianRounding.FormatQuantity(draft.Quantity!.Value),
                ["base_quantity"] = DianRounding.FormatQuantity(draft.Quantity!.Value),
                ["price_amount"] = draft.UnitPrice!.Value.ToString(Invariant),
                ["free_of_charge_indicator"] = draft.FreeOfCharge,
                ["line_extension_amount"] = DianRounding.FormatAmount(line.LineExtensionAmount),
                ["allowance_charges"] = new JsonArray(line.AllowanceCharges.Select(LegacyCharge).ToArray()),
                ["tax_totals"] = new JsonArray(line.TaxSubtotals.Select(LegacyTax).ToArray()),
            });
        }

        var totals = calculation.LegalMonetaryTotals;
        return new JsonObject
        {
            [spec.LinesField] = lines,
            ["tax_totals"] = new JsonArray(calculation.TaxTotals.SelectMany(group => group.TaxSubtotals).Select(LegacyTax).ToArray()),
            ["allowance_charges"] = new JsonArray(calculation.AllowanceCharges.Select(LegacyCharge).ToArray()),
            [spec.TotalsField] = new JsonObject
            {
                ["line_extension_amount"] = DianRounding.FormatAmount(totals.LineExtensionAmount),
                ["tax_exclusive_amount"] = DianRounding.FormatAmount(totals.TaxExclusiveAmount),
                ["tax_inclusive_amount"] = DianRounding.FormatAmount(totals.TaxInclusiveAmount),
                ["allowance_total_amount"] = DianRounding.FormatAmount(totals.AllowanceTotalAmount),
                ["charge_total_amount"] = DianRounding.FormatAmount(totals.ChargeTotalAmount),
                ["pre_paid_amount"] = DianRounding.FormatAmount(totals.PrepaidAmount),
                ["payable_amount"] = DianRounding.FormatAmount(totals.PayableAmount),
            },
        };
    }

    /// <summary>Los errores de un resultado, para el mensaje de una aserción.</summary>
    public static string Describe<T>(Result<T> result) =>
        result.IsSuccess ? "success" : string.Join("; ", result.Error.Fields.Select(field => $"{field.Field}: {field.Message}"));

    private static readonly string[] TotalsFields =
    [
        "line_extension_amount", "tax_exclusive_amount", "tax_inclusive_amount", "allowance_total_amount",
        "charge_total_amount", "payable_amount",
    ];

    private static JsonNode LegacyCharge(CalculatedAllowanceCharge item) => new JsonObject
    {
        ["charge_indicator"] = item.ChargeIndicator,
        ["amount"] = DianRounding.FormatAmount(item.Amount),
        ["base_amount"] = DianRounding.FormatAmount(item.BaseAmount),
    };

    private static JsonNode LegacyTax(CalculatedTaxSubtotal tax)
    {
        var node = new JsonObject
        {
            ["tax_id"] = tax.TaxId,
            ["taxable_amount"] = DianRounding.FormatAmount(tax.TaxableAmount),
            ["tax_amount"] = DianRounding.FormatAmount(tax.TaxAmount),
        };
        if (tax.Percent is { } percent)
        {
            node["percent"] = DianRounding.FormatAmount(percent);
        }

        if (tax.PerUnitAmount is { } perUnit)
        {
            node["per_unit_amount"] = DianRounding.FormatAmount(perUnit);
            node["base_unit_measure"] = DianRounding.FormatQuantity(tax.BaseUnitMeasure!.Value);
            node["unit_measure_id"] = tax.UnitMeasureId;
        }

        return node;
    }

    private static CalculationAllowanceChargeRequest AllowanceCharge(JsonObject item) => new()
    {
        ChargeIndicator = item["charge_indicator"]!.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? item["charge_indicator"]!.GetValue<bool>()
            : string.Equals(item["charge_indicator"]!.GetValue<string>(), "true", StringComparison.OrdinalIgnoreCase),
        Amount = Number(item, "amount"),
    };

    private static IEnumerable<string> DescribeCharge(string path, JsonObject item) =>
    [
        $"{path}.amount={Amount(Number(item, "amount"))}",
        $"{path}.base_amount={Amount(Number(item, "base_amount"))}",
    ];

    private static IEnumerable<string> DescribeCharge(string path, CalculatedAllowanceCharge item) =>
    [
        $"{path}.amount={Amount(item.Amount)}",
        $"{path}.base_amount={Amount(item.BaseAmount)}",
    ];

    private static IEnumerable<string> DescribeTax(string path, JsonObject tax) =>
    [
        $"{path}.tax_id={Number(tax, "tax_id")}",
        $"{path}.taxable_amount={Amount(Number(tax, "taxable_amount"))}",
        $"{path}.tax_amount={Amount(Number(tax, "tax_amount"))}",
        Number(tax, "per_unit_amount") is { } perUnit
            ? $"{path}.per_unit_amount={Amount(perUnit)} x {DianRounding.FormatQuantity(Number(tax, "base_unit_measure")!.Value)}"
            : $"{path}.percent={Amount(Number(tax, "percent"))}",
    ];

    private static IEnumerable<string> DescribeTax(string path, CalculatedTaxSubtotal tax) =>
    [
        $"{path}.tax_id={tax.TaxId}",
        $"{path}.taxable_amount={Amount(tax.TaxableAmount)}",
        $"{path}.tax_amount={Amount(tax.TaxAmount)}",
        tax.PerUnitAmount is { } perUnit
            ? $"{path}.per_unit_amount={Amount(perUnit)} x {DianRounding.FormatQuantity(tax.BaseUnitMeasure!.Value)}"
            : $"{path}.percent={Amount(tax.Percent)}",
    ];

    private static string Amount(decimal? value) => value is { } present ? DianRounding.FormatAmount(present) : "null";

    private static IEnumerable<JsonObject> Items(JsonObject parent, string name) =>
        parent[name] is JsonArray items ? items.Select(item => item!.AsObject()) : [];

    private static decimal? Number(JsonObject parent, string name) => parent[name] switch
    {
        null => null,
        var value when value.GetValueKind() == JsonValueKind.String => DianRounding.Parse(value.GetValue<string>()),
        var value when value.GetValueKind() == JsonValueKind.Number => DianRounding.Parse(value.ToJsonString()),
        _ => null,
    };

    private static string Name(string dir) =>
        Path.GetFileName(Path.GetDirectoryName(dir)) + "/" + Path.GetFileName(dir);

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
