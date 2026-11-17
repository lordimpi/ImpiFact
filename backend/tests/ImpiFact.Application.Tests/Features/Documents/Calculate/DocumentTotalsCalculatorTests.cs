using ImpiFact.Application.Features.Documents.Calculate;
using ImpiFact.Application.Features.Documents.Issue;
using static ImpiFact.Application.Tests.Features.Documents.Calculate.CalculationSamples;

namespace ImpiFact.Application.Tests.Features.Documents.Calculate;

/// <summary>
/// Reglas del cálculo de totales de <c>POST /api/v1/documents/calculate</c>: cada importe se redondea a 2 decimales
/// cuando se escribe en el XML (con <c>DianRounding</c>) y los totales suman esos valores ya redondeados, así que lo que
/// devuelve el cálculo, llevado a la emisión, siempre pasa el validador de totales de la DIAN (la Dirección de
/// Impuestos y Aduanas Nacionales).
/// </summary>
public sealed class DocumentTotalsCalculatorTests
{
    private const int Iva = 1;
    private const int Inc = 4;
    private const int Bags = 10;

    private static readonly IssueSpec[] AllSpecs = [IssueSpec.Invoice, IssueSpec.CreditNote, IssueSpec.DebitNote];

    [Test]
    public void The_line_value_uses_the_full_unit_price_and_is_rounded_once()
    {
        var calculation = Calculate(Draft(Line(3.333333m, 1234.567m, Percent(Iva, 19m))));

        var line = calculation.Lines[0];
        Assert.Multiple(() =>
        {
            Assert.That(line.LineExtensionAmount, Is.EqualTo(4115.22m));
            Assert.That(line.TaxSubtotals[0].TaxableAmount, Is.EqualTo(4115.22m));
            Assert.That(line.TaxSubtotals[0].TaxAmount, Is.EqualTo(781.89m));
            Assert.That(line.TaxSubtotals[0].Percent, Is.EqualTo(19.00m));
        });
    }

    [Test]
    public void Every_amount_has_two_decimals_so_the_json_shows_them()
    {
        var calculation = Calculate(Draft(Line(1m, 1000m, Percent(Iva, 19m))));

        Assert.Multiple(() =>
        {
            Assert.That(calculation.Lines[0].LineExtensionAmount.ToString(System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo("1000.00"));
            Assert.That(calculation.LegalMonetaryTotals.PayableAmount.ToString(System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo("1190.00"));
        });
    }

    [Test]
    public void A_line_discount_by_percent_and_a_charge_by_amount_change_the_line_value_and_the_tax_base()
    {
        var line = Line(4m, 250m, Percent(Iva, 19m)) with
        {
            AllowanceCharges = [Discount(percent: 10m), Charge(amount: 15.50m)],
        };

        var calculation = Calculate(Draft(line));

        var result = calculation.Lines[0];
        Assert.Multiple(() =>
        {
            Assert.That(result.AllowanceCharges.Select(item => (item.ChargeIndicator, item.Amount, item.BaseAmount)),
                Is.EqualTo(new[] { (false, 100.00m, 1000.00m), (true, 15.50m, 1000.00m) }));
            Assert.That(result.LineExtensionAmount, Is.EqualTo(915.50m));
            Assert.That(result.TaxSubtotals[0].TaxableAmount, Is.EqualTo(915.50m));
            Assert.That(result.TaxSubtotals[0].TaxAmount, Is.EqualTo(173.95m)); // 173.945 se redondea alejándose de cero.
        });
    }

    [Test]
    public void Global_discounts_and_charges_apply_to_the_payable_amount_but_not_to_the_taxes()
    {
        var draft = Draft(Line(1m, 1000m, Percent(Iva, 19m))) with
        {
            AllowanceCharges = [Discount(percent: 5m), Charge(amount: 20m)],
        };

        var calculation = Calculate(draft);

        var totals = calculation.LegalMonetaryTotals;
        Assert.Multiple(() =>
        {
            Assert.That(calculation.AllowanceCharges.Select(item => (item.Amount, item.BaseAmount)),
                Is.EqualTo(new[] { (50.00m, 1000.00m), (20.00m, 1000.00m) }));
            Assert.That(calculation.TaxTotals[0].TaxAmount, Is.EqualTo(190.00m));
            Assert.That(totals.TaxInclusiveAmount, Is.EqualTo(1190.00m));
            Assert.That(totals.AllowanceTotalAmount, Is.EqualTo(50.00m));
            Assert.That(totals.ChargeTotalAmount, Is.EqualTo(20.00m));
            Assert.That(totals.PayableAmount, Is.EqualTo(1160.00m));
        });
    }

    [Test]
    public void Prepaid_payments_are_totalled_but_do_not_reduce_the_payable_amount()
    {
        var draft = Draft(Line(1m, 1000m, Percent(Iva, 19m))) with
        {
            PrepaidPayments = [new CalculationPrepaidPaymentRequest { PaidAmount = 300m }, new CalculationPrepaidPaymentRequest { PaidAmount = 50.25m }],
        };

        var totals = Calculate(draft).LegalMonetaryTotals;

        Assert.Multiple(() =>
        {
            Assert.That(totals.PrepaidAmount, Is.EqualTo(350.25m));
            Assert.That(totals.PayableAmount, Is.EqualTo(1190.00m));
        });
    }

    [Test]
    public void Taxes_are_grouped_by_tax_and_rate_in_order_of_appearance_and_untaxed_lines_stay_out_of_the_tax_base()
    {
        var draft = Draft(
            Line(1m, 100m, Percent(Inc, 8m)),
            Line(2m, 50m, Percent(Iva, 19m)),
            Line(1m, 40m, Percent(Iva, 5m)),
            Line(1m, 30m, Percent(Iva, 19m), Percent(Inc, 8m)),
            Line(1m, 999m));

        var calculation = Calculate(draft);

        Assert.Multiple(() =>
        {
            Assert.That(calculation.TaxTotals.Select(group => (group.TaxId, group.TaxAmount)),
                Is.EqualTo(new[] { (Inc, 10.40m), (Iva, 26.70m) }));
            Assert.That(calculation.TaxTotals[0].TaxSubtotals.Select(tax => (tax.Percent, tax.TaxableAmount, tax.TaxAmount)),
                Is.EqualTo(new[] { ((decimal?)8.00m, 130.00m, 10.40m) }));
            Assert.That(calculation.TaxTotals[1].TaxSubtotals.Select(tax => (tax.Percent, tax.TaxableAmount, tax.TaxAmount)),
                Is.EqualTo(new[] { ((decimal?)19.00m, 130.00m, 24.70m), (5.00m, 40.00m, 2.00m) }));
            Assert.That(calculation.LegalMonetaryTotals.LineExtensionAmount, Is.EqualTo(1269.00m));
            Assert.That(calculation.LegalMonetaryTotals.TaxExclusiveAmount, Is.EqualTo(270.00m));
            Assert.That(calculation.LegalMonetaryTotals.TaxInclusiveAmount, Is.EqualTo(1306.10m));
        });
    }

    [Test]
    public void A_per_unit_tax_uses_the_line_quantity_unless_the_taxed_units_are_given()
    {
        var draft = Draft(
            Line(3m, 0m, PerUnit(Bags, 66m)),
            Line(1m, 0m, PerUnit(Bags, 66m, units: 5m)));

        var calculation = Calculate(draft);

        Assert.Multiple(() =>
        {
            Assert.That(calculation.Lines.Select(line => (line.TaxSubtotals[0].BaseUnitMeasure, line.TaxSubtotals[0].TaxAmount)),
                Is.EqualTo(new[] { ((decimal?)3m, 198.00m), (5m, 330.00m) }));
            Assert.That(calculation.TaxTotals[0].TaxSubtotals.Select(tax => (tax.PerUnitAmount, tax.BaseUnitMeasure, tax.UnitMeasureId, tax.TaxAmount)),
                Is.EqualTo(new[] { ((decimal?)66.00m, (decimal?)8m, (int?)70, 528.00m) }));
            Assert.That(calculation.TaxTotals[0].TaxSubtotals[0].Percent, Is.Null);
        });
    }

    [Test]
    public void A_free_of_charge_line_is_worth_zero()
    {
        var draft = Draft(Line(1m, 1000m, Percent(Iva, 19m)), Line(2m, 65000m) with { FreeOfCharge = true });

        var calculation = Calculate(draft);

        Assert.Multiple(() =>
        {
            Assert.That(calculation.Lines[1].LineExtensionAmount, Is.EqualTo(0.00m));
            Assert.That(calculation.LegalMonetaryTotals.LineExtensionAmount, Is.EqualTo(1000.00m));
            Assert.That(calculation.LegalMonetaryTotals.PayableAmount, Is.EqualTo(1190.00m));
        });
    }

    /// <summary>
    /// La regla AX07 compara a pesos enteros base × tarifa / 100 sin redondear con el impuesto escrito: 23.67 × 19 % =
    /// 4.4973 da 4 pesos, pero su redondeo normal (4.50) da 5 y la DIAN lo rechazaría. El cálculo escribe 4.49.
    /// </summary>
    [Test]
    public void A_line_tax_just_below_half_a_peso_is_written_one_cent_lower_so_the_dian_accepts_it()
    {
        var draft = Draft(Line(1m, 23.67m, Percent(Iva, 19m)));
        var calculation = Calculate(draft);
        var normalRounding = calculation with
        {
            Lines = [calculation.Lines[0] with { TaxSubtotals = [calculation.Lines[0].TaxSubtotals[0] with { TaxAmount = 4.50m }] }],
        };

        var issued = ValidateAsIssued(draft, calculation, IssueSpec.Invoice);
        var rejected = ValidateAsIssued(draft, normalRounding, IssueSpec.Invoice);

        Assert.Multiple(() =>
        {
            Assert.That(calculation.Lines[0].TaxSubtotals[0].TaxAmount, Is.EqualTo(4.49m));
            Assert.That(calculation.TaxTotals[0].TaxAmount, Is.EqualTo(4.49m));
            Assert.That(calculation.LegalMonetaryTotals.PayableAmount, Is.EqualTo(28.16m));
            Assert.That(issued.IsSuccess, Is.True, Describe(issued));
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.Error.Fields.Select(field => field.Field), Does.Contain("invoice_lines[0].tax_totals[0].tax_amount"));
        });
    }

    /// <summary>
    /// Por qué los totales suman los valores ya redondeados: dos líneas de 100.245 quedan en el XML como 100.25 cada
    /// una. Su suma sin redondear (200.49) incumple la regla FAU02 al compararla a pesos enteros (200 contra 201); la
    /// suma de lo escrito (200.50) la cumple.
    /// </summary>
    [Test]
    public void Summing_unrounded_line_values_can_cross_half_a_peso_and_the_dian_rejects_it()
    {
        var draft = Draft(Line(1m, 100.245m), Line(1m, 100.245m));
        var calculation = Calculate(draft);
        var unrounded = calculation with
        {
            LegalMonetaryTotals = calculation.LegalMonetaryTotals with
            {
                LineExtensionAmount = 200.49m, TaxInclusiveAmount = 200.49m, PayableAmount = 200.49m,
            },
        };

        var issued = ValidateAsIssued(draft, calculation, IssueSpec.Invoice);
        var rejected = ValidateAsIssued(draft, unrounded, IssueSpec.Invoice);

        Assert.Multiple(() =>
        {
            Assert.That(calculation.LegalMonetaryTotals.LineExtensionAmount, Is.EqualTo(200.50m));
            Assert.That(issued.IsSuccess, Is.True, Describe(issued));
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.Error.Fields.Select(field => field.Field), Does.Contain("legal_monetary_totals.line_extension_amount"));
        });
    }

    /// <summary>
    /// La propiedad central, con muchos borradores al azar (semilla fija, para que la prueba sea repetible): precios con
    /// hasta 4 decimales, cantidades fraccionarias, descuentos y cargos por valor y por porcentaje, IVA, INC, bolsas,
    /// descuentos y cargos globales y anticipos. Lo calculado, llevado a la emisión, pasa la validación de la DIAN en los
    /// tres tipos de documento. Los anticipos se prueban aparte (<see cref="CalculationSamples.ValidateWithPrepaid"/>)
    /// porque la emisión todavía no recibe su lista.
    /// </summary>
    [Test]
    public void Random_drafts_always_pass_the_issue_totals_validation_in_every_document_type()
    {
        var random = new Random(20261117);
        var failures = new List<string>();

        for (var sample = 0; sample < 400; sample++)
        {
            var draft = RandomDraft(random);
            var result = DocumentTotalsCalculator.Calculate(draft);
            if (!result.IsSuccess)
            {
                failures.Add($"sample {sample}: {Describe(result)}");
                continue;
            }

            foreach (var spec in AllSpecs)
            {
                var withoutPrepaid = draft with { PrepaidPayments = [] };
                var issued = ValidateAsIssued(withoutPrepaid, DocumentTotalsCalculator.Calculate(withoutPrepaid).Value, spec);
                var withPrepaid = ValidateWithPrepaid(draft, result.Value, spec);
                if (!issued.IsSuccess || !withPrepaid.IsSuccess)
                {
                    failures.Add($"sample {sample} ({spec.Kind}): {Describe(issued)} / with prepaid: {Describe(withPrepaid)}");
                }
            }
        }

        Assert.That(failures, Is.Empty);
    }

    [Test]
    public void Line_discounts_above_the_line_value_are_rejected()
    {
        var draft = Draft(Line(1m, 10m) with { AllowanceCharges = [Discount(amount: 10.01m)] });

        var result = DocumentTotalsCalculator.Calculate(draft);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error.Code, Is.EqualTo("validation.failed"));
        Assert.That(result.Error.Fields.Select(field => (field.Field, field.Message)), Is.EqualTo(new[]
        {
            ("lines[0].allowance_charges", "Los descuentos de la línea no pueden superar su valor (precio por cantidad más cargos)."),
        }));
    }

    [Test]
    public void Global_discounts_above_the_total_with_taxes_are_rejected()
    {
        var draft = Draft(Line(1m, 100m, Percent(Iva, 19m))) with { AllowanceCharges = [Discount(amount: 119.01m)] };

        var result = DocumentTotalsCalculator.Calculate(draft);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error.Fields.Select(field => (field.Field, field.Message)), Is.EqualTo(new[]
        {
            ("allowance_charges", "Los descuentos globales no pueden superar el total con impuestos más los cargos globales."),
        }));
    }

    private static DocumentTotalsCalculation Calculate(CalculateDocumentTotalsRequest draft)
    {
        var result = DocumentTotalsCalculator.Calculate(draft);
        Assert.That(result.IsSuccess, Is.True, Describe(result));
        return result.Value;
    }

    private static CalculateDocumentTotalsRequest Draft(params CalculationLineRequest[] lines) => new() { Lines = lines };

    private static CalculationLineRequest Line(decimal quantity, decimal unitPrice, params CalculationTaxRequest[] taxes) =>
        new() { Quantity = quantity, UnitPrice = unitPrice, Taxes = taxes };

    private static CalculationTaxRequest Percent(int taxId, decimal percent) => new() { TaxId = taxId, Percent = percent };

    private static CalculationTaxRequest PerUnit(int taxId, decimal amount, decimal? units = null) =>
        new() { TaxId = taxId, PerUnitAmount = amount, BaseUnitMeasure = units, UnitMeasureId = 70 };

    private static CalculationAllowanceChargeRequest Discount(decimal? amount = null, decimal? percent = null) =>
        new() { ChargeIndicator = false, Amount = amount, Percent = percent };

    private static CalculationAllowanceChargeRequest Charge(decimal? amount = null, decimal? percent = null) =>
        new() { ChargeIndicator = true, Amount = amount, Percent = percent };

    private static CalculateDocumentTotalsRequest RandomDraft(Random random)
    {
        var lines = Enumerable.Range(0, random.Next(1, 9)).Select(_ => RandomLine(random)).ToList();
        return new CalculateDocumentTotalsRequest
        {
            Lines = lines,
            AllowanceCharges = Enumerable.Range(0, random.Next(0, 3))
                .Select(_ => random.Next(2) == 0 ? Discount(percent: random.Next(0, 500) / 100m) : Charge(amount: random.Next(0, 100000) / 100m))
                .ToList(),
            PrepaidPayments = Enumerable.Range(0, random.Next(0, 2))
                .Select(_ => new CalculationPrepaidPaymentRequest { PaidAmount = random.Next(0, 100000) / 100m })
                .ToList(),
        };
    }

    private static CalculationLineRequest RandomLine(Random random)
    {
        var quantity = random.Next(1, 100000) / (decimal)Math.Pow(10, random.Next(0, 4));
        var unitPrice = random.Next(0, 10_000_000) / (decimal)Math.Pow(10, random.Next(0, 5));
        CalculationTaxRequest[][] taxChoices =
        [
            [], [Percent(Iva, 19m)], [Percent(Iva, 5m)], [Percent(Iva, 0m)], [Percent(Inc, 8m)],
            [Percent(Iva, 19m), Percent(Inc, 8m)], [PerUnit(Bags, 66m)], [Percent(Iva, 19m), PerUnit(Bags, 66m, units: random.Next(1, 10))],
        ];
        return new CalculationLineRequest
        {
            Quantity = quantity,
            UnitPrice = unitPrice,
            AllowanceCharges = Enumerable.Range(0, random.Next(0, 3))
                .Select(_ => random.Next(2) == 0 ? Discount(percent: random.Next(0, 3000) / 100m) : Charge(amount: random.Next(0, 10000) / 100m))
                .ToList(),
            Taxes = taxChoices[random.Next(taxChoices.Length)],
        };
    }
}
