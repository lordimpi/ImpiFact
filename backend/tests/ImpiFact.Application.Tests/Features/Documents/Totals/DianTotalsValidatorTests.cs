using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Totals;
using static ImpiFact.Application.Tests.Features.Documents.Totals.TotalsSamples;

namespace ImpiFact.Application.Tests.Features.Documents.Totals;

/// <summary>
/// Reglas de totales del Anexo Técnico de factura electrónica de la DIAN (Dirección de Impuestos y Aduanas
/// Nacionales), versión 1.9, sección 8. Cada regla que se exige tiene un caso que pasa y uno que falla por
/// tipo de documento; cada regla que no se exige tiene un caso que muestra que no rechaza.
/// </summary>
public sealed class DianTotalsValidatorTests
{
    private static readonly TotalsDocumentKind[] AllKinds =
        [TotalsDocumentKind.Invoice, TotalsDocumentKind.CreditNote, TotalsDocumentKind.DebitNote];

    private static readonly TotalsDocumentKind[] KindsWithPrepaidRule =
        [TotalsDocumentKind.Invoice, TotalsDocumentKind.DebitNote];

    [TestCaseSource(nameof(AllKinds))]
    public void Balanced_document_passes_and_returns_the_same_document(TotalsDocumentKind kind)
    {
        var document = Balanced(kind);

        var result = DianTotalsValidator.Validate(document);

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(result.Value, Is.SameAs(document));
    }

    [Test]
    public void Failure_is_a_validation_error()
    {
        var result = DianTotalsValidator.Validate(
            Balanced(TotalsDocumentKind.Invoice).WithTotals(t => t with { PayableAmount = 1360.00m }));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
        Assert.That(result.Error.Code, Is.EqualTo("validation.failed"));
    }

    // AU02: round(LineExtensionAmount) = round(sum(líneas LineExtensionAmount)).

    [TestCaseSource(nameof(AllKinds))]
    public void Au02_line_sum_within_the_same_whole_peso_passes(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind).WithLine(0, l => l with { LineExtensionAmount = 1000.49m }));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
    }

    [TestCaseSource(nameof(AllKinds))]
    public void Au02_line_sum_rounding_to_another_peso_fails(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind).WithLine(0, l => l with { LineExtensionAmount = 1000.50m }));

        Assert.That(HasError(result, TotalsField(kind, "line_extension_amount"), Rule(kind, "U02")), Is.True, Describe(result));
    }

    // AU06: round(LineExtensionAmount + sum(TaxTotal de cabecera)) = round(TaxInclusiveAmount).

    [TestCaseSource(nameof(AllKinds))]
    public void Au06_tax_inclusive_within_the_same_whole_peso_passes(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind).WithTotals(t => t with { TaxInclusiveAmount = 1388.49m, PayableAmount = 1358.49m }));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
    }

    [TestCaseSource(nameof(AllKinds))]
    public void Au06_tax_inclusive_rounding_to_another_peso_fails(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind).WithTotals(t => t with { TaxInclusiveAmount = 1388.50m, PayableAmount = 1358.50m }));

        Assert.That(HasError(result, TotalsField(kind, "tax_inclusive_amount"), Rule(kind, "U06")), Is.True, Describe(result));
        Assert.That(Errors(result), Has.Count.EqualTo(1), Describe(result));
    }

    // AU08: round(AllowanceTotalAmount) = round(sum(descuentos de cabecera)).

    [TestCaseSource(nameof(AllKinds))]
    public void Au08_discount_sum_within_the_same_whole_peso_passes(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind) with { AllowanceCharges = [new AllowanceChargeInput(false, 50.49m), new AllowanceChargeInput(true, 20.00m)] });

        Assert.That(result.IsSuccess, Is.True, Describe(result));
    }

    [TestCaseSource(nameof(AllKinds))]
    public void Au08_discount_sum_rounding_to_another_peso_fails(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind) with { AllowanceCharges = [new AllowanceChargeInput(false, 50.50m), new AllowanceChargeInput(true, 20.00m)] });

        Assert.That(HasError(result, TotalsField(kind, "allowance_total_amount"), Rule(kind, "U08")), Is.True, Describe(result));
        Assert.That(Errors(result), Has.Count.EqualTo(1), Describe(result));
    }

    [TestCaseSource(nameof(AllKinds))]
    public void Au08_discount_total_present_without_discounts_fails(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind) with { AllowanceCharges = [new AllowanceChargeInput(true, 20.00m)] });

        Assert.That(HasError(result, TotalsField(kind, "allowance_total_amount"), Rule(kind, "U08")), Is.True, Describe(result));
    }

    // AU10: round(ChargeTotalAmount) = round(sum(cargos de cabecera)).

    [TestCaseSource(nameof(AllKinds))]
    public void Au10_charge_sum_within_the_same_whole_peso_passes(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind) with { AllowanceCharges = [new AllowanceChargeInput(false, 50.00m), new AllowanceChargeInput(true, 19.50m)] });

        Assert.That(result.IsSuccess, Is.True, Describe(result));
    }

    [TestCaseSource(nameof(AllKinds))]
    public void Au10_charge_sum_rounding_to_another_peso_fails(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind) with { AllowanceCharges = [new AllowanceChargeInput(false, 50.00m), new AllowanceChargeInput(true, 20.50m)] });

        Assert.That(HasError(result, TotalsField(kind, "charge_total_amount"), Rule(kind, "U10")), Is.True, Describe(result));
        Assert.That(Errors(result), Has.Count.EqualTo(1), Describe(result));
    }

    // AU12 (factura y nota débito; la nota crédito no la tiene): si hay PrepaidAmount,
    // round(PrepaidAmount) = round(sum(PrepaidPayment/PaidAmount)).

    [TestCaseSource(nameof(KindsWithPrepaidRule))]
    public void Au12_prepaid_sum_within_the_same_whole_peso_passes(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind) with { PrepaidPayments = [new PrepaidPaymentInput(60.00m), new PrepaidPaymentInput(40.49m)] });

        Assert.That(result.IsSuccess, Is.True, Describe(result));
    }

    [TestCaseSource(nameof(KindsWithPrepaidRule))]
    public void Au12_prepaid_sum_rounding_to_another_peso_fails(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind) with { PrepaidPayments = [new PrepaidPaymentInput(60.00m), new PrepaidPaymentInput(40.50m)] });

        Assert.That(HasError(result, TotalsField(kind, "pre_paid_amount"), Rule(kind, "U12")), Is.True, Describe(result));
        Assert.That(Errors(result), Has.Count.EqualTo(1), Describe(result));
    }

    [TestCaseSource(nameof(KindsWithPrepaidRule))]
    public void Au12_is_not_checked_when_the_document_has_no_prepaid_total(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind).WithTotals(t => t with { PrepaidAmount = null }));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
    }

    [Test]
    public void Credit_note_has_no_prepaid_rule()
    {
        var result = DianTotalsValidator.Validate(
            Balanced(TotalsDocumentKind.CreditNote) with { PrepaidPayments = [new PrepaidPaymentInput(5.00m)] });

        Assert.That(result.IsSuccess, Is.True, Describe(result));
    }

    // AU14: round(TaxInclusiveAmount − AllowanceTotalAmount + ChargeTotalAmount) = round(PayableAmount),
    // con 0.00 para los totales que faltan; los anticipos no entran en la fórmula.

    [TestCaseSource(nameof(AllKinds))]
    public void Au14_payable_within_the_same_whole_peso_passes(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind).WithTotals(t => t with { PayableAmount = 1358.49m }));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
    }

    [TestCaseSource(nameof(AllKinds))]
    public void Au14_payable_rounding_to_another_peso_fails(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind).WithTotals(t => t with { PayableAmount = 1358.50m }));

        Assert.That(HasError(result, TotalsField(kind, "payable_amount"), Rule(kind, "U14")), Is.True, Describe(result));
        Assert.That(Errors(result), Has.Count.EqualTo(1), Describe(result));
    }

    [TestCaseSource(nameof(AllKinds))]
    public void Au14_missing_discount_and_charge_totals_count_as_zero(TotalsDocumentKind kind)
    {
        var document = Balanced(kind).WithTotals(t => t with
        {
            AllowanceTotalAmount = null,
            ChargeTotalAmount = null,
            PayableAmount = 1388.00m,
        }) with { AllowanceCharges = [] };

        Assert.That(DianTotalsValidator.Validate(document).IsSuccess, Is.True);

        var subtracted = DianTotalsValidator.Validate(document.WithTotals(t => t with { PayableAmount = 1358.00m }));
        Assert.That(HasError(subtracted, TotalsField(kind, "payable_amount"), Rule(kind, "U14")), Is.True, Describe(subtracted));
    }

    [TestCaseSource(nameof(AllKinds))]
    public void Au14_does_not_subtract_the_prepaid_amount(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind).WithTotals(t => t with { PayableAmount = 1258.00m }));

        Assert.That(HasError(result, TotalsField(kind, "payable_amount"), Rule(kind, "U14")), Is.True, Describe(result));
    }

    [Test]
    public void Au14_message_names_the_rule_and_the_rounded_values()
    {
        var result = DianTotalsValidator.Validate(
            Balanced(TotalsDocumentKind.Invoice).WithTotals(t => t with { PayableAmount = 1360.00m }));

        Assert.That(Errors(result), Is.EqualTo(new[]
        {
            new FieldError(
                "legal_monetary_totals.payable_amount",
                "Regla FAU14 de la DIAN: el valor a pagar debe ser igual al total con impuestos menos los descuentos " +
                "más los cargos. Redondeado a pesos se esperaba 1358 y se recibió 1360."),
        }));
    }

    // AS02: valor de cada cac:TaxTotal de cabecera = suma de sus subtotales (redondeado a pesos).

    [TestCaseSource(nameof(AllKinds))]
    public void As02_header_tax_total_matching_its_subtotals_passes(TotalsDocumentKind kind)
    {
        // Dos subtotales de IVA: el cac:TaxTotal vale 0.01 (0.005 + 0.005) y sus subtotales 0.01 + 0.01.
        var document = Balanced(kind) with
        {
            TaxTotals = [.. Balanced(kind).TaxTotals, IvaSubtotal(0.03m, 0.005m), IvaSubtotal(0.03m, 0.005m)],
        };

        var result = DianTotalsValidator.Validate(document);

        Assert.That(HasRule(result, Rule(kind, "S02")), Is.False, Describe(result));
    }

    [TestCaseSource(nameof(AllKinds))]
    public void As02_header_tax_total_drifting_from_its_subtotals_fails(TotalsDocumentKind kind)
    {
        // 200 subtotales de 0.005: el cac:TaxTotal vale 1.00, pero cada subtotal se escribe como 0.01 y suman 2.00.
        var document = Balanced(kind) with
        {
            TaxTotals = [.. Enumerable.Repeat(IvaSubtotal(0.03m, 0.005m), 200), BagsSubtotal(198.00m)],
        };

        var result = DianTotalsValidator.Validate(document);

        Assert.That(HasError(result, "tax_totals[0].tax_amount", Rule(kind, "S02")), Is.True, Describe(result));
    }

    // AX02: valor de cada cac:TaxTotal de una línea = suma de sus subtotales (redondeado a pesos, por impuesto).

    [TestCaseSource(nameof(AllKinds))]
    public void Ax02_line_tax_total_matching_its_subtotals_passes(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(Balanced(kind).WithLine(0, l => l with
        {
            TaxTotals = [IvaSubtotal(500.00m, 95.005m), IvaSubtotal(500.00m, 95.005m)],
        }));

        Assert.That(HasRule(result, Rule(kind, "X02")), Is.False, Describe(result));
    }

    [TestCaseSource(nameof(AllKinds))]
    public void Ax02_line_tax_total_drifting_from_its_subtotals_fails(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(Balanced(kind).WithLine(1, l => l with
        {
            TaxTotals = [BagsSubtotal(198.00m), .. Enumerable.Repeat(IvaSubtotal(0.03m, 0.005m), 200)],
        }));

        Assert.That(HasError(result, LinesField(kind) + "[1].tax_totals[1].tax_amount", Rule(kind, "X02")), Is.True, Describe(result));
    }

    // AX07: impuesto de cada subtotal de línea = base × tarifa / 100, o valor por unidad × cantidad.

    [TestCaseSource(nameof(AllKinds))]
    public void Ax07_line_tax_within_the_same_whole_peso_passes(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind).WithLine(0, l => l with { TaxTotals = [IvaSubtotal(1000.00m, 190.49m)] }));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
    }

    [TestCaseSource(nameof(AllKinds))]
    public void Ax07_line_tax_rounding_to_another_peso_fails(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind).WithLine(0, l => l with { TaxTotals = [IvaSubtotal(1000.00m, 190.50m)] }));

        Assert.That(HasError(result, LinesField(kind) + "[0].tax_totals[0].tax_amount", Rule(kind, "X07")), Is.True, Describe(result));
        Assert.That(Errors(result), Has.Count.EqualTo(1), Describe(result));
    }

    [TestCaseSource(nameof(AllKinds))]
    public void Ax07_per_unit_tax_matching_quantity_passes(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind).WithLine(1, l => l with { TaxTotals = [BagsSubtotal(198.49m, baseUnitMeasure: 7m)] }));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
    }

    [TestCaseSource(nameof(AllKinds))]
    public void Ax07_per_unit_tax_matching_base_unit_measure_passes(TotalsDocumentKind kind)
    {
        // Lectura del anexo para el INPP (PerUnitAmount × BaseUnitMeasure) y el IBUA (lo mismo dividido entre 100).
        var document = Balanced(kind) with
        {
            TaxTotals = [IvaSubtotal(1000.00m, 190.00m), BagsSubtotal(198.00m), BagsSubtotal(198.00m)],
        };
        document = document
            .WithTotals(t => t with { TaxInclusiveAmount = 1586.00m, PayableAmount = 1556.00m })
            .WithLine(1, l => l with
            {
                Quantity = 1m,
                TaxTotals = [BagsSubtotal(198.00m, baseUnitMeasure: 3m), BagsSubtotal(198.00m, baseUnitMeasure: 300m)],
            });

        var result = DianTotalsValidator.Validate(document);

        Assert.That(result.IsSuccess, Is.True, Describe(result));
    }

    [TestCaseSource(nameof(AllKinds))]
    public void Ax07_per_unit_tax_matching_no_reading_fails(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind).WithLine(1, l => l with { TaxTotals = [BagsSubtotal(264.00m)] }));

        Assert.That(HasError(result, LinesField(kind) + "[1].tax_totals[0].tax_amount", Rule(kind, "X07")), Is.True, Describe(result));
    }

    [TestCaseSource(nameof(AllKinds))]
    public void Ax07_message_names_the_rule_and_the_rounded_values(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind).WithLine(0, l => l with { TaxTotals = [IvaSubtotal(1000.00m, 193.00m)] }));

        Assert.That(Errors(result), Is.EqualTo(new[]
        {
            new FieldError(
                LinesField(kind) + "[0].tax_totals[0].tax_amount",
                $"Regla {Rule(kind, "X07")} de la DIAN: el valor del impuesto debe ser igual a la base gravable por la " +
                "tarifa dividida entre 100. Redondeado a pesos se esperaba 190 y se recibió 193."),
        }));
    }

    // VLR01: los valores monetarios y porcentajes deben ser positivos (cero se admite).

    [TestCaseSource(nameof(AllKinds))]
    public void Vlr01_zero_amounts_pass(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(Balanced(kind).WithLine(1, l => l with
        {
            AllowanceCharges = [new AllowanceChargeInput(false, 0.00m)],
        }));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
    }

    [TestCaseSource(nameof(AllKinds))]
    public void Vlr01_negative_amounts_fail(TotalsDocumentKind kind)
    {
        var document = Balanced(kind) with
        {
            AllowanceCharges = [new AllowanceChargeInput(false, 50.00m), new AllowanceChargeInput(true, -20.00m)],
            PrepaidPayments = [new PrepaidPaymentInput(-100.00m)],
        };
        document = document
            .WithTotals(t => t with { ChargeTotalAmount = -20.00m, PrepaidAmount = -100.00m, PayableAmount = 1318.00m })
            .WithLine(0, l => l with
            {
                PriceAmount = -500.00m,
                AllowanceCharges = [new AllowanceChargeInput(false, -1.00m)],
                TaxTotals = [new TaxSubtotalInput { TaxId = Iva, TaxableAmount = 1000.00m, TaxAmount = 190.00m, Percent = -19.00m }],
            });

        var result = DianTotalsValidator.Validate(document);

        var rule = "VLR01";
        var lines = LinesField(kind);
        Assert.Multiple(() =>
        {
            Assert.That(HasError(result, TotalsField(kind, "charge_total_amount"), rule), Is.True, Describe(result));
            Assert.That(HasError(result, TotalsField(kind, "pre_paid_amount"), rule), Is.True, Describe(result));
            Assert.That(HasError(result, "allowance_charges[1].amount", rule), Is.True, Describe(result));
            Assert.That(HasError(result, "prepaid_payments[0].paidamount", rule), Is.True, Describe(result));
            Assert.That(HasError(result, lines + "[0].price_amount", rule), Is.True, Describe(result));
            Assert.That(HasError(result, lines + "[0].allowance_charges[0].amount", rule), Is.True, Describe(result));
            Assert.That(HasError(result, lines + "[0].tax_totals[0].percent", rule), Is.True, Describe(result));
        });
    }

    [Test]
    public void Vlr01_message_names_the_rule_and_the_value()
    {
        var result = DianTotalsValidator.Validate(Balanced(TotalsDocumentKind.Invoice).WithLine(0, l => l with { PriceAmount = -500.00m }));

        Assert.That(Errors(result), Is.EqualTo(new[]
        {
            new FieldError(
                "invoice_lines[0].price_amount",
                "Regla VLR01 de la DIAN: los valores monetarios y los porcentajes no pueden ser negativos. Se recibió -500.00."),
        }));
    }

    // Redondeo: importes a 2 decimales como en el XML y luego round() de XPath a pesos enteros.

    [Test]
    public void Amounts_are_compared_as_written_in_the_xml()
    {
        // 1000.4949 se escribe 1000.49 (redondea a 1000); 1000.495 se escribe 1000.50 (redondea a 1001).
        var kept = DianTotalsValidator.Validate(
            Balanced(TotalsDocumentKind.Invoice).WithLine(0, l => l with { LineExtensionAmount = 1000.4949m }));
        var moved = DianTotalsValidator.Validate(
            Balanced(TotalsDocumentKind.Invoice).WithLine(0, l => l with { LineExtensionAmount = 1000.495m }));

        Assert.That(kept.IsSuccess, Is.True, Describe(kept));
        Assert.That(HasRule(moved, "FAU02"), Is.True, Describe(moved));
    }

    [TestCase(2.49, 2)]
    [TestCase(2.5, 3)]
    [TestCase(0.5, 1)]
    [TestCase(-2.5, -2)]
    [TestCase(-2.51, -3)]
    [TestCase(-0.49, 0)]
    [TestCase(4918.12, 4918)]
    public void XPath_round_takes_halves_toward_positive_infinity(decimal value, decimal expected)
    {
        Assert.That(DianTotalsValidator.XPathRound(value), Is.EqualTo(expected));
    }

    [Test]
    public void Every_broken_rule_is_reported()
    {
        var result = DianTotalsValidator.Validate(Balanced(TotalsDocumentKind.Invoice)
            .WithTotals(t => t with { LineExtensionAmount = 900.00m, PayableAmount = 1000.00m })
            .WithLine(0, l => l with { TaxTotals = [IvaSubtotal(1000.00m, 150.00m)] }));

        Assert.That(Errors(result).Select(e => e.Field), Is.EquivalentTo(new[]
        {
            "legal_monetary_totals.line_extension_amount",
            "legal_monetary_totals.tax_inclusive_amount",
            "legal_monetary_totals.payable_amount",
            "invoice_lines[0].tax_totals[0].tax_amount",
        }), Describe(result));
    }

    // Ejemplo de php-rules (sección 1, punto 2): 4133.69 + 784.43 = 4918.12 frente a 4918.11.

    [Test]
    public void Php_rules_example_rounds_to_the_same_peso_and_passes()
    {
        var document = new TotalsDocument
        {
            Kind = TotalsDocumentKind.Invoice,
            MonetaryTotals = new MonetaryTotalsInput
            {
                LineExtensionAmount = 4133.69m,
                TaxExclusiveAmount = 4133.69m,
                TaxInclusiveAmount = 4918.11m,
                PayableAmount = 4918.11m,
            },
            TaxTotals = [IvaSubtotal(4126.72m, 784.08m), new TaxSubtotalInput { TaxId = Iva, TaxableAmount = 6.97m, TaxAmount = 0.35m, Percent = 5.00m }],
            Lines =
            [
                new TotalsLineInput { Quantity = 3.333333m, LineExtensionAmount = 4115.22m, PriceAmount = 1234.57m, BaseQuantity = 3.333333m, TaxTotals = [IvaSubtotal(4115.22m, 781.89m)] },
                new TotalsLineInput { Quantity = 1m, LineExtensionAmount = 11.50m, PriceAmount = 11.50m, BaseQuantity = 1m, TaxTotals = [IvaSubtotal(11.50m, 2.19m)] },
                new TotalsLineInput { Quantity = 7m, LineExtensionAmount = 6.97m, PriceAmount = 1.00m, BaseQuantity = 7m, TaxTotals = [new TaxSubtotalInput { TaxId = Iva, TaxableAmount = 6.97m, TaxAmount = 0.35m, Percent = 5.00m }] },
            ],
        };

        var result = DianTotalsValidator.Validate(document);

        Assert.That(result.IsSuccess, Is.True, Describe(result));
    }

    // Reglas del anexo que no se exigen (se confirman en la fase 1b contra la DIAN real).

    [TestCaseSource(nameof(AllKinds))]
    public void Av06_line_amount_against_price_and_base_quantity_is_not_checked(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind).WithLine(0, l => l with { PriceAmount = 1.00m, BaseQuantity = 1m }));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
    }

    [TestCaseSource(nameof(AllKinds))]
    public void As07_header_tax_against_base_and_rate_is_not_checked(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind) with { TaxTotals = [IvaSubtotal(500.00m, 190.00m), BagsSubtotal(198.00m)] });

        Assert.That(result.IsSuccess, Is.True, Describe(result));
    }

    [TestCaseSource(nameof(AllKinds))]
    public void Au04_tax_exclusive_amount_is_not_checked(TotalsDocumentKind kind)
    {
        var result = DianTotalsValidator.Validate(
            Balanced(kind).WithTotals(t => t with { TaxExclusiveAmount = 5000.00m }));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
    }

    [Test]
    public void General_two_peso_margin_is_not_applied()
    {
        // Una diferencia de 1.00, dentro del margen de ±2.00 que el anexo menciona sin fórmula, se rechaza.
        var result = DianTotalsValidator.Validate(
            Balanced(TotalsDocumentKind.Invoice).WithTotals(t => t with { PayableAmount = 1359.00m }));

        Assert.That(HasRule(result, "FAU14"), Is.True, Describe(result));
    }

    [Test]
    public void Five_peso_iva_margin_is_not_applied()
    {
        // Un IVA de línea 3 pesos por encima del cálculo, dentro del margen de ±5 del texto, se rechaza.
        var result = DianTotalsValidator.Validate(
            Balanced(TotalsDocumentKind.Invoice).WithLine(0, l => l with { TaxTotals = [IvaSubtotal(1000.00m, 193.00m)] }));

        Assert.That(HasRule(result, "FAX07"), Is.True, Describe(result));
    }
}
