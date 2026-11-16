using System.Globalization;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Dian.Rounding;

namespace ImpiFact.Application.Features.Documents.Totals;

/// <summary>
/// Valida los totales de una factura, nota crédito o nota débito con las reglas de rechazo del Anexo Técnico
/// de factura electrónica de la DIAN (Dirección de Impuestos y Aduanas Nacionales), versión 1.9
/// (Resolución 000165 de 2023), sección 8. Los casos de uso lo llaman antes de tomar el consecutivo.
/// </summary>
/// <remarks>
/// <para>
/// Decisión 18 del PRD: se rechaza con 422 solo lo que la DIAN rechazaría; si no, el XML lleva los valores
/// del cliente tal como vinieron. Por eso el validador no corrige nada: devuelve el mismo documento o un
/// error de validación con un <see cref="FieldError"/> por cada regla incumplida.
/// </para>
/// <para>
/// Cómo compara: cada importe se redondea primero a 2 decimales, como queda escrito en el XML (UBL, el
/// formato XML de la DIAN) con <see cref="DianRounding"/>, y luego los dos lados de la regla se redondean a
/// pesos enteros con la función <c>round()</c> de XPath que usan las reglas del anexo (ver
/// <see cref="XPathRound"/>). Un total opcional que el XML no lleva (<see langword="null"/>) no se valida con
/// su propia regla, porque la regla del anexo se evalúa sobre ese elemento, y vale 0.00 en la regla del
/// valor a pagar. Cuando el texto de una regla admite una lectura estricta y otra más amplia, se aplica la
/// más amplia: lo que la incumple también incumple la estricta, así que la DIAN lo rechazaría con cualquiera
/// de las dos.
/// </para>
/// <para>
/// Reglas que se exigen (prefijo FA en la factura, CA en la nota crédito y DA en la nota débito; en la nota
/// débito los totales van en cac:RequestedMonetaryTotal):
/// </para>
/// <list type="bullet">
/// <item>AU02: valor bruto = suma del valor bruto de las líneas.</item>
/// <item>AU06: total con impuestos = valor bruto + suma de los cac:TaxTotal de la cabecera.</item>
/// <item>AU08: total de descuentos = suma de los descuentos de la cabecera.</item>
/// <item>AU10: total de cargos = suma de los cargos de la cabecera.</item>
/// <item>AU12 (no existe CAU12): total de anticipos = suma de los anticipos.</item>
/// <item>AU14: valor a pagar = total con impuestos − total de descuentos + total de cargos. Los anticipos no
/// entran en la fórmula.</item>
/// <item>AS02 y AX02: valor de cada cac:TaxTotal (de la cabecera y de cada línea) = suma de sus subtotales.
/// Con el contrato legado el cac:TaxTotal se arma sumando los subtotales sin redondear (php-rules, sección
/// 3.2), así que solo falla si el redondeo a 2 decimales de muchos subtotales se acumula hasta medio peso.</item>
/// <item>AX07: impuesto de cada subtotal de línea = base gravable × tarifa / 100; en los impuestos de valor
/// fijo, valor por unidad × cantidad de la línea, o valor por unidad × unidades gravadas (INPP), o eso
/// mismo dividido entre 100 (IBUA): el anexo da las tres fórmulas y basta con que se cumpla una.</item>
/// <item>VLR01: ningún importe ni porcentaje puede ser negativo (cero se admite).</item>
/// </list>
/// <para>
/// Reglas que no se exigen porque el anexo es ambiguo; se confirman en la fase 1b contra la DIAN real:
/// </para>
/// <list type="bullet">
/// <item>AV06: valor de la línea = precio × cantidad base − descuentos + cargos de la línea (la fórmula usa
/// Price/BaseQuantity y no la cantidad facturada).</item>
/// <item>AS07 en la cabecera: la fórmula del anexo multiplica por la tarifa sin dividir entre 100.</item>
/// <item>AU04: base gravable total. FAU04 y DAU04 suman las bases de todos los subtotales de las líneas
/// (una base cuenta dos veces si la línea tiene dos impuestos) y CAU04 solo las de <c>TaxTotal[1]</c>.</item>
/// <item>El margen general de ±2.00 y el de ±5 pesos en el IVA: el anexo los menciona sin fórmula, así que
/// no se suman a la comparación a pesos enteros.</item>
/// <item>PayableRoundingAmount: no tiene regla propia; ImpiFact no lo escribe porque el contrato legado no
/// lo trae.</item>
/// </list>
/// </remarks>
public static class DianTotalsValidator
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static Result<TotalsDocument> Validate(TotalsDocument document)
    {
        var context = new RuleContext(document.Kind);
        var totals = document.MonetaryTotals;
        var totalsField = document.Kind == TotalsDocumentKind.DebitNote ? "requested_monetary_totals" : "legal_monetary_totals";
        var linesField = document.Kind switch
        {
            TotalsDocumentKind.Invoice => "invoice_lines",
            TotalsDocumentKind.CreditNote => "credit_note_lines",
            _ => "debit_note_lines",
        };

        CheckNotNegative(context, totals, totalsField, document, linesField);

        var lineExtension = Amount(totals.LineExtensionAmount);
        context.Compare(
            "AU02",
            $"{totalsField}.line_extension_amount",
            "el valor bruto debe ser igual a la suma del valor bruto de las líneas",
            expected: document.Lines.Sum(line => Amount(line.LineExtensionAmount)),
            received: lineExtension);

        var headerTaxTotals = TaxTotals(document.TaxTotals);
        context.Compare(
            "AU06",
            $"{totalsField}.tax_inclusive_amount",
            "el total con impuestos debe ser igual al valor bruto más los impuestos de la cabecera",
            expected: lineExtension + headerTaxTotals.Sum(group => group.Amount),
            received: Amount(totals.TaxInclusiveAmount));

        if (totals.AllowanceTotalAmount is { } allowanceTotal)
        {
            context.Compare(
                "AU08",
                $"{totalsField}.allowance_total_amount",
                "el total de descuentos debe ser igual a la suma de los descuentos de la cabecera",
                expected: document.AllowanceCharges.Where(item => !item.IsCharge).Sum(item => Amount(item.Amount)),
                received: Amount(allowanceTotal));
        }

        if (totals.ChargeTotalAmount is { } chargeTotal)
        {
            context.Compare(
                "AU10",
                $"{totalsField}.charge_total_amount",
                "el total de cargos debe ser igual a la suma de los cargos de la cabecera",
                expected: document.AllowanceCharges.Where(item => item.IsCharge).Sum(item => Amount(item.Amount)),
                received: Amount(chargeTotal));
        }

        // La nota crédito no tiene la regla CAU12.
        if (totals.PrepaidAmount is { } prepaidTotal && document.Kind != TotalsDocumentKind.CreditNote)
        {
            context.Compare(
                "AU12",
                $"{totalsField}.pre_paid_amount",
                "el total de anticipos debe ser igual a la suma de los anticipos",
                expected: document.PrepaidPayments.Sum(payment => Amount(payment.PaidAmount)),
                received: Amount(prepaidTotal));
        }

        context.Compare(
            "AU14",
            $"{totalsField}.payable_amount",
            "el valor a pagar debe ser igual al total con impuestos menos los descuentos más los cargos",
            expected: Amount(totals.TaxInclusiveAmount) - Amount(totals.AllowanceTotalAmount ?? 0m) + Amount(totals.ChargeTotalAmount ?? 0m),
            received: Amount(totals.PayableAmount));

        CheckTaxTotals(context, "AS02", headerTaxTotals, "tax_totals");

        for (var i = 0; i < document.Lines.Count; i++)
        {
            var line = document.Lines[i];
            var lineField = $"{linesField}[{i}]";
            CheckTaxTotals(context, "AX02", TaxTotals(line.TaxTotals), $"{lineField}.tax_totals");
            for (var j = 0; j < line.TaxTotals.Count; j++)
            {
                CheckLineTax(context, line, line.TaxTotals[j], $"{lineField}.tax_totals[{j}].tax_amount");
            }
        }

        return context.Errors.Count == 0 ? document : Error.Validation(context.Errors);
    }

    /// <summary>
    /// Función <c>round()</c> de XPath, la que usan las reglas del anexo: el entero más cercano y, si hay dos
    /// a la misma distancia, el más cercano a infinito positivo (2.5 da 3 y −2.5 da −2). No es el redondeo de
    /// <see cref="DianRounding"/>, que fija los decimales con que se escribe cada valor en el XML.
    /// </summary>
    internal static decimal XPathRound(decimal value) => decimal.Floor(value + 0.5m);

    /// <summary>Un importe como queda escrito en el XML: con 2 decimales.</summary>
    private static decimal Amount(decimal value) => DianRounding.RoundAmount(value);

    /// <summary>
    /// Agrupa los subtotales por impuesto como el cac:TaxTotal del XML, cuyo valor es la suma de los
    /// subtotales sin redondear, escrita con 2 decimales. Conserva la posición del primer subtotal del grupo
    /// para nombrar el campo.
    /// </summary>
    private static List<TaxTotalGroup> TaxTotals(IReadOnlyList<TaxSubtotalInput> subtotals) =>
        subtotals
            .Select((subtotal, index) => (subtotal, index))
            .GroupBy(item => item.subtotal.TaxId)
            .Select(group => new TaxTotalGroup(
                group.First().index,
                Amount(group.Sum(item => item.subtotal.TaxAmount)),
                group.Sum(item => Amount(item.subtotal.TaxAmount))))
            .ToList();

    private static void CheckTaxTotals(RuleContext context, string rule, List<TaxTotalGroup> groups, string field)
    {
        foreach (var group in groups)
        {
            context.Compare(
                rule,
                $"{field}[{group.FirstIndex}].tax_amount",
                "el valor total del impuesto debe ser igual a la suma de sus subtotales, cada uno con 2 decimales",
                expected: group.SubtotalSum,
                received: group.Amount);
        }
    }

    private static void CheckLineTax(RuleContext context, TotalsLineInput line, TaxSubtotalInput subtotal, string field)
    {
        var received = Amount(subtotal.TaxAmount);
        if (subtotal.PerUnitAmount is { } perUnit)
        {
            var amount = Amount(perUnit);
            var units = DianRounding.RoundQuantity(subtotal.BaseUnitMeasure ?? 0m);
            decimal[] readings =
            [
                amount * DianRounding.RoundQuantity(line.Quantity),
                amount * units,
                amount * units / 100m,
            ];
            if (!readings.Any(reading => XPathRound(reading) == XPathRound(received)))
            {
                context.Add(
                    context.Code("AX07"),
                    field,
                    "el valor del impuesto debe ser igual al valor por unidad por la cantidad de la línea",
                    XPathRound(readings[0]),
                    XPathRound(received));
            }

            return;
        }

        if (subtotal.Percent is { } percent)
        {
            context.Compare(
                "AX07",
                field,
                "el valor del impuesto debe ser igual a la base gravable por la tarifa dividida entre 100",
                expected: Amount(subtotal.TaxableAmount) * Amount(percent) / 100m,
                received: received);
        }
    }

    /// <summary>VLR01: los importes y porcentajes, tal como quedan en el XML, no pueden ser negativos.</summary>
    private static void CheckNotNegative(
        RuleContext context, MonetaryTotalsInput totals, string totalsField, TotalsDocument document, string linesField)
    {
        context.NotNegative($"{totalsField}.line_extension_amount", totals.LineExtensionAmount);
        context.NotNegative($"{totalsField}.tax_exclusive_amount", totals.TaxExclusiveAmount);
        context.NotNegative($"{totalsField}.tax_inclusive_amount", totals.TaxInclusiveAmount);
        context.NotNegative($"{totalsField}.allowance_total_amount", totals.AllowanceTotalAmount);
        context.NotNegative($"{totalsField}.charge_total_amount", totals.ChargeTotalAmount);
        context.NotNegative($"{totalsField}.pre_paid_amount", totals.PrepaidAmount);
        context.NotNegative($"{totalsField}.payable_amount", totals.PayableAmount);
        CheckTaxesNotNegative(context, document.TaxTotals, "tax_totals");
        CheckAllowanceChargesNotNegative(context, document.AllowanceCharges, "allowance_charges");
        for (var i = 0; i < document.PrepaidPayments.Count; i++)
        {
            context.NotNegative($"prepaid_payments[{i}].paidamount", document.PrepaidPayments[i].PaidAmount);
        }

        for (var i = 0; i < document.Lines.Count; i++)
        {
            var line = document.Lines[i];
            var lineField = $"{linesField}[{i}]";
            context.NotNegative($"{lineField}.line_extension_amount", line.LineExtensionAmount);
            context.NotNegative($"{lineField}.price_amount", line.PriceAmount);
            CheckAllowanceChargesNotNegative(context, line.AllowanceCharges, $"{lineField}.allowance_charges");
            CheckTaxesNotNegative(context, line.TaxTotals, $"{lineField}.tax_totals");
        }
    }

    private static void CheckTaxesNotNegative(RuleContext context, IReadOnlyList<TaxSubtotalInput> subtotals, string field)
    {
        for (var i = 0; i < subtotals.Count; i++)
        {
            var subtotal = subtotals[i];
            context.NotNegative($"{field}[{i}].taxable_amount", subtotal.TaxableAmount);
            context.NotNegative($"{field}[{i}].tax_amount", subtotal.TaxAmount);
            context.NotNegative($"{field}[{i}].percent", subtotal.Percent);
            context.NotNegative($"{field}[{i}].per_unit_amount", subtotal.PerUnitAmount);
        }
    }

    private static void CheckAllowanceChargesNotNegative(RuleContext context, IReadOnlyList<AllowanceChargeInput> items, string field)
    {
        for (var i = 0; i < items.Count; i++)
        {
            context.NotNegative($"{field}[{i}].amount", items[i].Amount);
        }
    }

    private sealed record TaxTotalGroup(int FirstIndex, decimal Amount, decimal SubtotalSum);

    /// <summary>Acumula los errores y pone el prefijo de las reglas según el tipo de documento.</summary>
    private sealed class RuleContext(TotalsDocumentKind kind)
    {
        public List<FieldError> Errors { get; } = [];

        /// <summary>Código de la regla en el anexo: FA (factura), CA (nota crédito) o DA (nota débito).</summary>
        public string Code(string suffix) => kind switch
        {
            TotalsDocumentKind.Invoice => "F" + suffix,
            TotalsDocumentKind.CreditNote => "C" + suffix,
            _ => "D" + suffix,
        };

        public void Compare(string rule, string field, string description, decimal expected, decimal received)
        {
            var roundedExpected = XPathRound(expected);
            var roundedReceived = XPathRound(received);
            if (roundedExpected != roundedReceived)
            {
                Add(Code(rule), field, description, roundedExpected, roundedReceived);
            }
        }

        public void Add(string code, string field, string description, decimal expected, decimal received) =>
            Errors.Add(new FieldError(
                field,
                $"Regla {code} de la DIAN: {description}. Redondeado a pesos se esperaba " +
                $"{expected.ToString("0", Invariant)} y se recibió {received.ToString("0", Invariant)}."));

        public void NotNegative(string field, decimal? value)
        {
            if (value is { } present && DianRounding.RoundAmount(present) < 0m)
            {
                Errors.Add(new FieldError(
                    field,
                    "Regla VLR01 de la DIAN: los valores monetarios y los porcentajes no pueden ser negativos. " +
                    $"Se recibió {DianRounding.FormatAmount(present)}."));
            }
        }
    }
}
