using FluentValidation;

namespace ImpiFact.Application.Features.Billing.StorePlan;

/// <summary>
/// Reglas de <c>PlanRequest.php</c>: <c>id</c> de 1 a 3 dígitos; las cuatro cantidades obligatorias, de 1 a 10 dígitos;
/// periodo 1, 2 o 3. Con los textos de Laravel en español, como los demás validadores del contrato heredado. Dos
/// diferencias: el id 0 queda reservado (en el PHP era el plan por defecto sin límites, y en ImpiFact «sin plan» ya
/// significa eso) y el nombre y las observaciones tienen el largo de sus columnas.
/// </summary>
public sealed class StorePlanValidator : AbstractValidator<StorePlanCommand>
{
    public const int MaxId = 999;
    public const long MaxQuantity = 9_999_999_999;
    public const int MaxNameLength = 100;
    public const int MaxObservationsLength = 500;

    public StorePlanValidator()
    {
        RuleFor(command => command.Id)
            .NotNull().WithMessage(PlanMessages.Required("id"))
            .GreaterThanOrEqualTo(1).WithMessage(PlanMessages.MinNumber("id", 1))
            .LessThanOrEqualTo(MaxId).WithMessage(PlanMessages.DigitsBetween("id", 1, 3))
            .OverridePropertyName("id");

        Quantity(RuleFor(command => command.QtyDocsInvoice), "qty_docs_invoice");
        Quantity(RuleFor(command => command.QtyDocsPayroll), "qty_docs_payroll");
        Quantity(RuleFor(command => command.QtyDocsRadian), "qty_docs_radian");
        Quantity(RuleFor(command => command.QtyDocsDs), "qty_docs_ds");

        Period(RuleFor(command => command.Period));

        RuleFor(command => command.Name)
            .MaximumLength(MaxNameLength).WithMessage(PlanMessages.MaxLength("name", MaxNameLength))
            .OverridePropertyName("name");

        RuleFor(command => command.Observations)
            .MaximumLength(MaxObservationsLength).WithMessage(PlanMessages.MaxLength("observations", MaxObservationsLength))
            .OverridePropertyName("observations");
    }

    /// <summary>Una cantidad de documentos obligatoria, de 0 a 10 dígitos (la comparten el alta y el guardado por id).</summary>
    internal static void Quantity<T>(IRuleBuilderInitial<T, long?> rule, string name) =>
        rule
            .NotNull().WithMessage(PlanMessages.Required(name))
            .InclusiveBetween(0, MaxQuantity).WithMessage(PlanMessages.DigitsBetween(name, 1, 10))
            .OverridePropertyName(name);

    /// <summary>Periodo obligatorio: 1 mensual, 2 anual o 3 paquete.</summary>
    internal static void Period<T>(IRuleBuilderInitial<T, int?> rule) =>
        rule
            .NotNull().WithMessage(PlanMessages.Required("period"))
            .Must(period => period is null or 1 or 2 or 3).WithMessage(PlanMessages.In("period"))
            .OverridePropertyName("period");
}
