using FluentValidation;
using FluentValidation.Results;

namespace ImpiFact.Application.Features.Documents.Calculate;

/// <summary>
/// Valida el borrador de <c>POST /api/v1/documents/calculate</c> antes de calcular. Cada error nombra el campo con su
/// ruta en la solicitud (<c>lines[0].taxes[1].percent</c>) y explica el motivo en español. Los límites de decimales son
/// los del XML de la DIAN (la Dirección de Impuestos y Aduanas Nacionales): 2 en importes y porcentajes, 6 en
/// cantidades; así ningún valor recibido cambia en silencio al escribirse. El precio unitario no tiene límite de
/// decimales porque el valor de la línea se calcula con el precio completo.
/// </summary>
public sealed class CalculateDocumentTotalsValidator : AbstractValidator<CalculateDocumentTotalsRequest>
{
    public const int MaxLines = 1000;

    /// <summary>Máximo de descuentos y cargos en la cabecera o en una línea, y de anticipos.</summary>
    public const int MaxAllowanceCharges = 100;

    public const int MaxTaxesPerLine = 20;

    /// <summary>Tope de importes y precios: deja margen para multiplicar y sumar sin desbordar un decimal.</summary>
    public const decimal MaxAmount = 999_999_999_999_999m;

    public const decimal MaxQuantity = 999_999_999m;

    public CalculateDocumentTotalsValidator() =>
        RuleFor(request => request).Custom((request, context) => Validate(request, new Failures(context)));

    private static void Validate(CalculateDocumentTotalsRequest request, Failures failures)
    {
        if (request.Lines is not { Count: > 0 } lines)
        {
            failures.Add("lines", "El documento debe tener al menos una línea.");
        }
        else if (lines.Count > MaxLines)
        {
            failures.Add("lines", $"El documento admite hasta {MaxLines} líneas.");
        }
        else
        {
            for (var i = 0; i < lines.Count; i++)
            {
                Line(failures, $"lines[{i}]", lines[i]);
            }
        }

        AllowanceCharges(failures, "allowance_charges", request.AllowanceCharges);
        var prepaid = request.PrepaidPayments ?? [];
        if (!failures.WithinCount("prepaid_payments", prepaid.Count, MaxAllowanceCharges))
        {
            return;
        }

        for (var i = 0; i < prepaid.Count; i++)
        {
            failures.Amount($"prepaid_payments[{i}].paid_amount", prepaid[i].PaidAmount, required: true);
        }
    }

    private static void Line(Failures failures, string path, CalculationLineRequest line)
    {
        if (line.Quantity is not { } quantity)
        {
            failures.Required($"{path}.quantity");
        }
        else if (quantity <= 0m)
        {
            failures.Add($"{path}.quantity", "La cantidad debe ser mayor que cero.");
        }
        else if (quantity > MaxQuantity)
        {
            failures.Add($"{path}.quantity", $"La cantidad no puede ser mayor que {MaxQuantity}.");
        }
        else if (!HasDecimals(quantity, 6))
        {
            failures.Add($"{path}.quantity", "La cantidad admite hasta 6 decimales.");
        }

        if (line.UnitPrice is not { } price)
        {
            failures.Required($"{path}.unit_price");
        }
        else if (price < 0m)
        {
            failures.Add($"{path}.unit_price", "El precio unitario no puede ser negativo.");
        }
        else if (price > MaxAmount)
        {
            failures.Add($"{path}.unit_price", $"El precio unitario no puede ser mayor que {MaxAmount}.");
        }

        if (line.FreeOfCharge && line.AllowanceCharges is { Count: > 0 })
        {
            failures.Add($"{path}.allowance_charges", "Una línea gratuita no lleva descuentos ni cargos.");
        }
        else
        {
            AllowanceCharges(failures, $"{path}.allowance_charges", line.AllowanceCharges);
        }

        var taxes = line.Taxes ?? [];
        if (!failures.WithinCount($"{path}.taxes", taxes.Count, MaxTaxesPerLine))
        {
            return;
        }

        for (var j = 0; j < taxes.Count; j++)
        {
            Tax(failures, $"{path}.taxes[{j}]", taxes[j], line.FreeOfCharge);
        }
    }

    private static void Tax(Failures failures, string path, CalculationTaxRequest tax, bool freeOfCharge)
    {
        if (tax.TaxId is not { } taxId)
        {
            failures.Required($"{path}.tax_id");
        }
        else if (taxId <= 0)
        {
            failures.Add($"{path}.tax_id", "El campo tax_id debe ser mayor que cero.");
        }

        switch (tax)
        {
            case { Percent: not null, PerUnitAmount: not null }:
                failures.Add(path, "El impuesto lleva percent o per_unit_amount, no los dos.");
                return;
            case { Percent: null, PerUnitAmount: null }:
                failures.Add(path, "El impuesto debe llevar percent o per_unit_amount.");
                return;
            case { Percent: { } percent }:
                if (freeOfCharge)
                {
                    failures.Add(path, "Una línea gratuita no lleva impuestos porcentuales; su valor es 0.");
                    return;
                }

                failures.Percent($"{path}.percent", percent);
                return;
        }

        failures.Amount($"{path}.per_unit_amount", tax.PerUnitAmount, required: true);
        if (tax.BaseUnitMeasure is not { } units)
        {
            return;
        }

        if (units <= 0m)
        {
            failures.Add($"{path}.base_unit_measure", "Las unidades gravadas deben ser mayores que cero.");
        }
        else if (units > MaxQuantity)
        {
            failures.Add($"{path}.base_unit_measure", $"Las unidades gravadas no pueden ser más de {MaxQuantity}.");
        }
        else if (!HasDecimals(units, 6))
        {
            failures.Add($"{path}.base_unit_measure", "Las unidades gravadas admiten hasta 6 decimales.");
        }
    }

    private static void AllowanceCharges(Failures failures, string path, IReadOnlyList<CalculationAllowanceChargeRequest>? items)
    {
        if (items is null || !failures.WithinCount(path, items.Count, MaxAllowanceCharges))
        {
            return;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var at = $"{path}[{i}]";
            if (item.Amount is not null && item.Percent is not null)
            {
                failures.Add(at, "El descuento o cargo lleva amount o percent, no los dos.");
            }
            else if (item.Amount is null && item.Percent is null)
            {
                failures.Add(at, "El descuento o cargo debe llevar amount o percent.");
            }
            else if (item.ChargeIndicator is null)
            {
                failures.Required($"{at}.charge_indicator");
            }
            else if (item.Percent is { } percent)
            {
                failures.Percent($"{at}.percent", percent);
            }
            else
            {
                failures.Amount($"{at}.amount", item.Amount, required: true);
            }
        }
    }

    /// <summary>Verdadero si el valor no tiene más decimales significativos que los indicados (1.50 tiene uno).</summary>
    private static bool HasDecimals(decimal value, int decimals) => decimal.Round(value, decimals) == value;

    /// <summary>Agrega los errores con la ruta exacta del campo en la solicitud.</summary>
    private sealed class Failures(ValidationContext<CalculateDocumentTotalsRequest> context)
    {
        public void Add(string field, string message) => context.AddFailure(new ValidationFailure(field, message));

        public void Required(string field) => Add(field, $"El campo {field[(field.LastIndexOf('.') + 1)..]} es obligatorio.");

        public bool WithinCount(string field, int count, int max)
        {
            if (count <= max)
            {
                return true;
            }

            Add(field, $"Admite hasta {max} elementos.");
            return false;
        }

        public void Percent(string field, decimal percent)
        {
            if (percent is < 0m or > 100m)
            {
                Add(field, "El porcentaje debe estar entre 0 y 100.");
            }
            else if (!HasDecimals(percent, 2))
            {
                Add(field, "El porcentaje admite hasta 2 decimales.");
            }
        }

        public void Amount(string field, decimal? amount, bool required)
        {
            if (amount is not { } value)
            {
                if (required)
                {
                    Required(field);
                }
            }
            else if (value < 0m)
            {
                Add(field, "El valor no puede ser negativo.");
            }
            else if (value > MaxAmount)
            {
                Add(field, $"El valor no puede ser mayor que {MaxAmount}.");
            }
            else if (!HasDecimals(value, 2))
            {
                Add(field, "El valor admite hasta 2 decimales.");
            }
        }
    }
}
