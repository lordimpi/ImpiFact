using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Totals;
using ImpiFact.Dian.Rounding;

namespace ImpiFact.Application.Features.Documents.Calculate;

/// <summary>
/// Calcula los valores de un documento a partir de sus líneas, con las mismas reglas que
/// <see cref="DianTotalsValidator"/> exige al emitir (Anexo Técnico de factura electrónica de la DIAN, la Dirección de
/// Impuestos y Aduanas Nacionales, versión 1.9). Es cálculo puro: no toma consecutivo, no guarda nada, no llama a la
/// DIAN ni consume el plan.
/// </summary>
/// <remarks>
/// <para>
/// Cada importe se redondea una sola vez, al quedar como se escribe en el XML (UBL, el formato XML de la DIAN), con
/// <see cref="DianRounding"/>; los totales suman esos valores ya redondeados, igual que las reglas del anexo, que se
/// evalúan sobre el XML. Así lo calculado siempre cumple AU02, AU06, AU08, AU10, AU12, AU14, AS02, AX02 y AX07, y no
/// solo al comparar a pesos enteros. Sumar los valores sin redondear (lo que hacía el PHP con lo que mandara el
/// cliente) puede dejar un total un centavo al otro lado del medio peso, y la DIAN lo rechazaría.
/// </para>
/// <list type="bullet">
/// <item>Valor de la línea = precio × cantidad − descuentos + cargos de la línea, con el precio completo (sin
/// redondear) y redondeado al final; 0 en una línea gratuita. La base de sus descuentos y cargos es precio ×
/// cantidad.</item>
/// <item>Impuesto porcentual de la línea = valor de la línea × tarifa / 100 (AX07); su base gravable es el valor de la
/// línea. Impuesto por unidad = valor por unidad × unidades gravadas (por omisión, la cantidad de la línea). Si el
/// producto termina entre ,495 y ,5 se escribe un centavo menos (ver <see cref="LineTaxAmount"/>).</item>
/// <item>Impuestos del documento: un grupo por impuesto (cac:TaxTotal) con un subtotal por tarifa o por valor por
/// unidad (cac:TaxSubtotal), sumando las líneas; el grupo vale la suma de sus subtotales (AS02).</item>
/// <item>Valor bruto = suma del valor de las líneas (AU02); base gravable = suma del valor de las líneas con algún
/// impuesto; total con impuestos = valor bruto + impuestos (AU06).</item>
/// <item>Descuentos y cargos globales sobre el valor bruto; no cambian los impuestos (AU08, AU10). Anticipos sumados
/// (AU12). Valor a pagar = total con impuestos − descuentos + cargos, sin restar los anticipos (AU14).</item>
/// </list>
/// <para>
/// Recibe una solicitud ya validada por <see cref="CalculateDocumentTotalsValidator"/>. Solo rechaza lo que da negativo
/// (regla VLR01): una línea con más descuentos que valor o un documento con más descuentos globales que total.
/// </para>
/// </remarks>
internal static class DocumentTotalsCalculator
{
    public static Result<DocumentTotalsCalculation> Calculate(CalculateDocumentTotalsRequest request)
    {
        var errors = new List<FieldError>();
        var drafts = request.Lines ?? [];
        var lines = new List<CalculatedLine>(drafts.Count);
        for (var i = 0; i < drafts.Count; i++)
        {
            var line = CalculateLine(drafts[i]);
            if (line.LineExtensionAmount < 0m)
            {
                errors.Add(new FieldError($"lines[{i}].allowance_charges",
                    "Los descuentos de la línea no pueden superar su valor (precio por cantidad más cargos)."));
            }

            lines.Add(line);
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var lineExtension = lines.Sum(line => line.LineExtensionAmount);
        var taxTotals = GroupTaxes(lines);
        var taxInclusive = lineExtension + taxTotals.Sum(group => group.TaxAmount);
        var globals = (request.AllowanceCharges ?? []).Select(item => AllowanceCharge(item, lineExtension)).ToList();
        var allowanceTotal = globals.Where(item => !item.ChargeIndicator).Sum(item => item.Amount);
        var chargeTotal = globals.Where(item => item.ChargeIndicator).Sum(item => item.Amount);
        var payable = taxInclusive - allowanceTotal + chargeTotal;
        if (payable < 0m)
        {
            return Error.Validation([new FieldError("allowance_charges",
                "Los descuentos globales no pueden superar el total con impuestos más los cargos globales.")]);
        }

        return new DocumentTotalsCalculation
        {
            Lines = lines,
            TaxTotals = taxTotals,
            AllowanceCharges = globals,
            LegalMonetaryTotals = new CalculatedMonetaryTotals
            {
                LineExtensionAmount = Money(lineExtension),
                TaxExclusiveAmount = Money(lines.Where(line => line.TaxSubtotals.Count > 0).Sum(line => line.LineExtensionAmount)),
                TaxInclusiveAmount = Money(taxInclusive),
                AllowanceTotalAmount = Money(allowanceTotal),
                ChargeTotalAmount = Money(chargeTotal),
                PrepaidAmount = Money((request.PrepaidPayments ?? []).Sum(payment => Money(payment.PaidAmount!.Value))),
                PayableAmount = Money(payable),
            },
        };
    }

    private static CalculatedLine CalculateLine(CalculationLineRequest draft)
    {
        var quantity = draft.Quantity!.Value;
        // Precio completo por cantidad, sin redondear: se redondea una sola vez, en el valor de la línea.
        var gross = draft.FreeOfCharge ? 0m : draft.UnitPrice!.Value * quantity;
        var charges = (draft.AllowanceCharges ?? []).Select(item => AllowanceCharge(item, gross)).ToList();
        var lineExtension = Money(gross
            - charges.Where(item => !item.ChargeIndicator).Sum(item => item.Amount)
            + charges.Where(item => item.ChargeIndicator).Sum(item => item.Amount));
        return new CalculatedLine
        {
            LineExtensionAmount = lineExtension,
            AllowanceCharges = charges,
            TaxSubtotals = (draft.Taxes ?? []).Select(tax => LineTax(tax, lineExtension, quantity)).ToList(),
        };
    }

    private static CalculatedTaxSubtotal LineTax(CalculationTaxRequest tax, decimal lineExtension, decimal quantity)
    {
        if (tax.PerUnitAmount is { } perUnit)
        {
            var amount = Money(perUnit);
            var units = DianRounding.RoundQuantity(tax.BaseUnitMeasure ?? quantity);
            return new CalculatedTaxSubtotal
            {
                TaxId = tax.TaxId!.Value,
                TaxableAmount = lineExtension,
                TaxAmount = LineTaxAmount(amount * units),
                PerUnitAmount = amount,
                BaseUnitMeasure = units,
                UnitMeasureId = tax.UnitMeasureId,
            };
        }

        var percent = Money(tax.Percent!.Value);
        return new CalculatedTaxSubtotal
        {
            TaxId = tax.TaxId!.Value,
            TaxableAmount = lineExtension,
            TaxAmount = LineTaxAmount(lineExtension * percent / 100m),
            Percent = percent,
        };
    }

    /// <summary>
    /// El impuesto de una línea con 2 decimales que cumple la regla AX07. Esa regla compara a pesos enteros el producto
    /// sin redondear (base × tarifa / 100, o valor por unidad × unidades) con el impuesto escrito en el XML. Cuando el
    /// producto termina entre ,495 y ,5 (por ejemplo 4.497), el redondeo normal (4.50) pasa al peso siguiente (5 contra
    /// 4) y la DIAN lo rechazaría; en ese caso se escribe un centavo menos (4.49), el valor más cercano que la cumple.
    /// </summary>
    private static decimal LineTaxAmount(decimal exact)
    {
        var amount = Money(exact);
        return DianTotalsValidator.XPathRound(amount) == DianTotalsValidator.XPathRound(exact) ? amount : Money(amount - 0.01m);
    }

    /// <summary>Un descuento o cargo: su valor, o el porcentaje sobre la base redondeado a 2 decimales.</summary>
    private static CalculatedAllowanceCharge AllowanceCharge(CalculationAllowanceChargeRequest item, decimal baseAmount)
    {
        var roundedBase = Money(baseAmount);
        return new CalculatedAllowanceCharge
        {
            ChargeIndicator = item.ChargeIndicator!.Value,
            Amount = item.Percent is { } percent ? Money(roundedBase * percent / 100m) : Money(item.Amount!.Value),
            BaseAmount = roundedBase,
        };
    }

    /// <summary>
    /// Agrupa los subtotales de las líneas como el XML: un cac:TaxTotal por impuesto y, dentro, un cac:TaxSubtotal por
    /// tarifa (o por valor por unidad y su unidad), en el orden en que aparecen por primera vez.
    /// </summary>
    private static List<CalculatedTaxTotal> GroupTaxes(List<CalculatedLine> lines)
    {
        var groups = new List<(int TaxId, List<CalculatedTaxSubtotal> Subtotals)>();
        foreach (var tax in lines.SelectMany(line => line.TaxSubtotals))
        {
            var groupIndex = groups.FindIndex(group => group.TaxId == tax.TaxId);
            if (groupIndex < 0)
            {
                groups.Add((tax.TaxId, []));
                groupIndex = groups.Count - 1;
            }

            var subtotals = groups[groupIndex].Subtotals;
            var index = subtotals.FindIndex(subtotal => SameRate(subtotal, tax));
            if (index < 0)
            {
                subtotals.Add(tax with { TaxableAmount = 0m, TaxAmount = 0m, BaseUnitMeasure = tax.PerUnitAmount is null ? null : 0m });
                index = subtotals.Count - 1;
            }

            var current = subtotals[index];
            subtotals[index] = current with
            {
                TaxableAmount = current.TaxableAmount + tax.TaxableAmount,
                TaxAmount = current.TaxAmount + tax.TaxAmount,
                BaseUnitMeasure = current.BaseUnitMeasure + tax.BaseUnitMeasure,
            };
        }

        return groups.Select(group => new CalculatedTaxTotal
        {
            TaxId = group.TaxId,
            TaxAmount = Money(group.Subtotals.Sum(subtotal => subtotal.TaxAmount)),
            TaxSubtotals = group.Subtotals.Select(subtotal => subtotal with
            {
                TaxableAmount = Money(subtotal.TaxableAmount),
                TaxAmount = Money(subtotal.TaxAmount),
            }).ToList(),
        }).ToList();
    }

    private static bool SameRate(CalculatedTaxSubtotal a, CalculatedTaxSubtotal b) =>
        a.PerUnitAmount is null
            ? b.PerUnitAmount is null && a.Percent == b.Percent
            : a.PerUnitAmount == b.PerUnitAmount && a.UnitMeasureId == b.UnitMeasureId;

    /// <summary>
    /// Un importe como queda escrito en el XML, con <see cref="DianRounding"/>: 2 decimales, también en la escala del
    /// número, para que el JSON muestre <c>1000.00</c> y no <c>1000</c>.
    /// </summary>
    private static decimal Money(decimal value) => DianRounding.Parse(DianRounding.FormatAmount(value));
}
