using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Totals;

namespace ImpiFact.Application.Tests.Features.Documents.Totals;

/// <summary>
/// Documento de ejemplo que cuadra: una línea con IVA del 19 % (1000.00 + 190.00), una línea de bolsas con
/// impuesto de valor fijo (3 × 66.00 = 198.00), un descuento global de 50.00, un cargo global de 20.00 y un
/// anticipo de 100.00. Total con impuestos 1388.00 y total a pagar 1388.00 − 50.00 + 20.00 = 1358.00.
/// </summary>
internal static class TotalsSamples
{
    public const int Iva = 1;
    public const int Bags = 10;

    public static TotalsDocument Balanced(TotalsDocumentKind kind) => new()
    {
        Kind = kind,
        MonetaryTotals = new MonetaryTotalsInput
        {
            LineExtensionAmount = 1000.00m,
            TaxExclusiveAmount = 1000.00m,
            TaxInclusiveAmount = 1388.00m,
            AllowanceTotalAmount = 50.00m,
            ChargeTotalAmount = 20.00m,
            PrepaidAmount = 100.00m,
            PayableAmount = 1358.00m,
        },
        TaxTotals = [IvaSubtotal(1000.00m, 190.00m), BagsSubtotal(198.00m)],
        AllowanceCharges = [new AllowanceChargeInput(false, 50.00m), new AllowanceChargeInput(true, 20.00m)],
        PrepaidPayments = [new PrepaidPaymentInput(100.00m)],
        Lines =
        [
            new TotalsLineInput
            {
                Quantity = 2m,
                LineExtensionAmount = 1000.00m,
                PriceAmount = 500.00m,
                BaseQuantity = 1m,
                TaxTotals = [IvaSubtotal(1000.00m, 190.00m)],
            },
            new TotalsLineInput
            {
                Quantity = 3m,
                LineExtensionAmount = 0.00m,
                PriceAmount = 0.00m,
                BaseQuantity = 3m,
                TaxTotals = [BagsSubtotal(198.00m)],
            },
        ],
    };

    public static TaxSubtotalInput IvaSubtotal(decimal taxable, decimal tax) =>
        new() { TaxId = Iva, TaxableAmount = taxable, TaxAmount = tax, Percent = 19.00m };

    public static TaxSubtotalInput BagsSubtotal(decimal tax, decimal baseUnitMeasure = 3m) =>
        new() { TaxId = Bags, TaxableAmount = 0.00m, TaxAmount = tax, PerUnitAmount = 66.00m, BaseUnitMeasure = baseUnitMeasure };

    public static TotalsDocument WithTotals(this TotalsDocument document, Func<MonetaryTotalsInput, MonetaryTotalsInput> change) =>
        document with { MonetaryTotals = change(document.MonetaryTotals) };

    public static TotalsDocument WithLine(this TotalsDocument document, int index, Func<TotalsLineInput, TotalsLineInput> change) =>
        document with { Lines = document.Lines.Select((line, i) => i == index ? change(line) : line).ToList() };

    /// <summary>Prefijo de las reglas del anexo técnico: FA (factura), CA (nota crédito) o DA (nota débito).</summary>
    public static string Rule(TotalsDocumentKind kind, string suffix) => kind switch
    {
        TotalsDocumentKind.Invoice => "FA" + suffix,
        TotalsDocumentKind.CreditNote => "CA" + suffix,
        _ => "DA" + suffix,
    };

    public static string TotalsField(TotalsDocumentKind kind, string name) =>
        (kind == TotalsDocumentKind.DebitNote ? "requested_monetary_totals." : "legal_monetary_totals.") + name;

    public static string LinesField(TotalsDocumentKind kind) => kind switch
    {
        TotalsDocumentKind.Invoice => "invoice_lines",
        TotalsDocumentKind.CreditNote => "credit_note_lines",
        _ => "debit_note_lines",
    };

    public static IReadOnlyList<FieldError> Errors(Result<TotalsDocument> result) =>
        result.IsSuccess ? [] : result.Error.Fields;

    /// <summary>Verdadero si hay un error en el campo cuyo mensaje nombra la regla.</summary>
    public static bool HasError(Result<TotalsDocument> result, string field, string rule) =>
        Errors(result).Any(error => error.Field == field && error.Message.Contains($"Regla {rule} ", StringComparison.Ordinal));

    /// <summary>Verdadero si algún error nombra la regla.</summary>
    public static bool HasRule(Result<TotalsDocument> result, string rule) =>
        Errors(result).Any(error => error.Message.Contains($"Regla {rule} ", StringComparison.Ordinal));

    public static string Describe(Result<TotalsDocument> result) =>
        string.Join(Environment.NewLine, Errors(result).Select(error => $"{error.Field}: {error.Message}"));
}
