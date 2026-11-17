using ImpiFact.Application.Features.Documents.Calculate;

namespace ImpiFact.Application.Tests.Features.Documents.Calculate;

/// <summary>
/// Validación del borrador de <c>POST /api/v1/documents/calculate</c>: cada campo inválido se nombra con su ruta en la
/// solicitud (<c>lines[0].taxes[1].percent</c>) y un mensaje en español.
/// </summary>
public sealed class CalculateDocumentTotalsValidatorTests
{
    private static readonly CalculateDocumentTotalsValidator Validator = new();

    [Test]
    public void A_valid_draft_has_no_errors()
    {
        Assert.That(Errors(Valid()), Is.Empty);
    }

    [Test]
    public void The_draft_needs_at_least_one_line()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Errors(new CalculateDocumentTotalsRequest()), Is.EqualTo(new[] { "lines: El documento debe tener al menos una línea." }));
            Assert.That(Errors(new CalculateDocumentTotalsRequest { Lines = [] }), Is.EqualTo(new[] { "lines: El documento debe tener al menos una línea." }));
        });
    }

    [Test]
    public void Too_many_lines_are_rejected()
    {
        var request = new CalculateDocumentTotalsRequest
        {
            Lines = Enumerable.Repeat(Valid().Lines![0], CalculateDocumentTotalsValidator.MaxLines + 1).ToList(),
        };

        Assert.That(Errors(request), Is.EqualTo(new[] { $"lines: El documento admite hasta {CalculateDocumentTotalsValidator.MaxLines} líneas." }));
    }

    [Test]
    public void Quantity_and_unit_price_are_required_and_bounded()
    {
        var request = WithLine(new CalculationLineRequest { Quantity = 0m, UnitPrice = -1m });
        var missing = WithLine(new CalculationLineRequest());
        var tooPrecise = WithLine(new CalculationLineRequest { Quantity = 1.0000001m, UnitPrice = 1m });

        Assert.Multiple(() =>
        {
            Assert.That(Errors(request), Is.EqualTo(new[]
            {
                "lines[0].quantity: La cantidad debe ser mayor que cero.",
                "lines[0].unit_price: El precio unitario no puede ser negativo.",
            }));
            Assert.That(Errors(missing), Is.EqualTo(new[]
            {
                "lines[0].quantity: El campo quantity es obligatorio.",
                "lines[0].unit_price: El campo unit_price es obligatorio.",
            }));
            Assert.That(Errors(tooPrecise), Is.EqualTo(new[] { "lines[0].quantity: La cantidad admite hasta 6 decimales." }));
        });
    }

    [Test]
    public void A_tax_is_either_a_percent_or_a_per_unit_amount()
    {
        var request = WithLine(Line() with
        {
            Taxes =
            [
                new CalculationTaxRequest { TaxId = 1, Percent = 19m, PerUnitAmount = 66m },
                new CalculationTaxRequest { TaxId = 1 },
                new CalculationTaxRequest { Percent = 19m },
                new CalculationTaxRequest { TaxId = 1, Percent = 100.01m },
                new CalculationTaxRequest { TaxId = 1, Percent = 19.005m },
                new CalculationTaxRequest { TaxId = 10, PerUnitAmount = 66m, BaseUnitMeasure = 0m },
            ],
        });

        Assert.That(Errors(request), Is.EqualTo(new[]
        {
            "lines[0].taxes[0]: El impuesto lleva percent o per_unit_amount, no los dos.",
            "lines[0].taxes[1]: El impuesto debe llevar percent o per_unit_amount.",
            "lines[0].taxes[2].tax_id: El campo tax_id es obligatorio.",
            "lines[0].taxes[3].percent: El porcentaje debe estar entre 0 y 100.",
            "lines[0].taxes[4].percent: El porcentaje admite hasta 2 decimales.",
            "lines[0].taxes[5].base_unit_measure: Las unidades gravadas deben ser mayores que cero.",
        }));
    }

    [Test]
    public void A_discount_or_charge_has_its_indicator_and_either_an_amount_or_a_percent()
    {
        var request = WithLine(Line() with
        {
            AllowanceCharges =
            [
                new CalculationAllowanceChargeRequest { ChargeIndicator = false, Amount = 1m, Percent = 1m },
                new CalculationAllowanceChargeRequest { ChargeIndicator = false },
                new CalculationAllowanceChargeRequest { Amount = 1m },
                new CalculationAllowanceChargeRequest { ChargeIndicator = true, Amount = 1.001m },
            ],
        }) with
        {
            AllowanceCharges = [new CalculationAllowanceChargeRequest { ChargeIndicator = false, Percent = -1m }],
        };

        Assert.That(Errors(request), Is.EqualTo(new[]
        {
            "lines[0].allowance_charges[0]: El descuento o cargo lleva amount o percent, no los dos.",
            "lines[0].allowance_charges[1]: El descuento o cargo debe llevar amount o percent.",
            "lines[0].allowance_charges[2].charge_indicator: El campo charge_indicator es obligatorio.",
            "lines[0].allowance_charges[3].amount: El valor admite hasta 2 decimales.",
            "allowance_charges[0].percent: El porcentaje debe estar entre 0 y 100.",
        }));
    }

    [Test]
    public void A_free_of_charge_line_takes_no_discounts_charges_or_percent_taxes()
    {
        var request = WithLine(Line() with
        {
            FreeOfCharge = true,
            AllowanceCharges = [new CalculationAllowanceChargeRequest { ChargeIndicator = false, Amount = 1m }],
            Taxes = [new CalculationTaxRequest { TaxId = 1, Percent = 19m }, new CalculationTaxRequest { TaxId = 10, PerUnitAmount = 66m }],
        });

        Assert.That(Errors(request), Is.EqualTo(new[]
        {
            "lines[0].allowance_charges: Una línea gratuita no lleva descuentos ni cargos.",
            "lines[0].taxes[0]: Una línea gratuita no lleva impuestos porcentuales; su valor es 0.",
        }));
    }

    [Test]
    public void Prepaid_payments_need_a_non_negative_amount()
    {
        var request = Valid() with
        {
            PrepaidPayments = [new CalculationPrepaidPaymentRequest(), new CalculationPrepaidPaymentRequest { PaidAmount = -5m }],
        };

        Assert.That(Errors(request), Is.EqualTo(new[]
        {
            "prepaid_payments[0].paid_amount: El campo paid_amount es obligatorio.",
            "prepaid_payments[1].paid_amount: El valor no puede ser negativo.",
        }));
    }

    private static CalculateDocumentTotalsRequest Valid() => WithLine(Line());

    private static CalculationLineRequest Line() => new()
    {
        Quantity = 2m,
        UnitPrice = 1000m,
        Taxes = [new CalculationTaxRequest { TaxId = 1, Percent = 19m }],
    };

    private static CalculateDocumentTotalsRequest WithLine(CalculationLineRequest line) => new() { Lines = [line] };

    private static List<string> Errors(CalculateDocumentTotalsRequest request) =>
        Validator.Validate(request).Errors.Select(error => $"{error.PropertyName}: {error.ErrorMessage}").ToList();
}
