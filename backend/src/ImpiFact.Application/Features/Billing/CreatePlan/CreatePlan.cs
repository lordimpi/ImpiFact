using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Billing.StorePlan;
using ImpiFact.Domain.Billing;

namespace ImpiFact.Application.Features.Billing.CreatePlan;

/// <summary>
/// Crea un plan nuevo con el id que asigna el servidor (<c>POST /api/v1/plans</c>), sin equivalente en el PHP, donde
/// quien llamaba elegía el id. Los campos son los de <see cref="StorePlanCommand"/> sin el id, y el nombre es
/// obligatorio. Es una operación de plataforma.
/// </summary>
public sealed record CreatePlanCommand : ICommand, ICompanyOptional
{
    /// <summary>Nombre único del plan.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Facturas y notas crédito y débito que incluye el plan en cada periodo.</summary>
    [JsonPropertyName("qty_docs_invoice")]
    public long? QtyDocsInvoice { get; init; }

    /// <summary>Documentos de nómina electrónica que incluye el plan en cada periodo.</summary>
    [JsonPropertyName("qty_docs_payroll")]
    public long? QtyDocsPayroll { get; init; }

    /// <summary>Eventos RADIAN que incluye el plan en cada periodo.</summary>
    [JsonPropertyName("qty_docs_radian")]
    public long? QtyDocsRadian { get; init; }

    /// <summary>Documentos soporte que incluye el plan en cada periodo.</summary>
    [JsonPropertyName("qty_docs_ds")]
    public long? QtyDocsDs { get; init; }

    /// <summary>1 mensual, 2 anual, 3 paquete.</summary>
    [JsonPropertyName("period")]
    public int? Period { get; init; }

    /// <summary>Activo o no; verdadero si no llega. Acepta también 1 y 0, como el guardado por id.</summary>
    [JsonPropertyName("state")]
    [JsonConverter(typeof(LaravelBooleanConverter))]
    public bool? State { get; init; }

    [JsonPropertyName("observations")]
    public string? Observations { get; init; }
}

/// <summary>Las reglas del guardado por id (<see cref="StorePlanValidator"/>) sin el id y con el nombre obligatorio.</summary>
public sealed class CreatePlanValidator : AbstractValidator<CreatePlanCommand>
{
    public CreatePlanValidator()
    {
        RuleFor(command => command.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage(PlanMessages.Required("name"))
            .MaximumLength(StorePlanValidator.MaxNameLength)
            .WithMessage(PlanMessages.MaxLength("name", StorePlanValidator.MaxNameLength))
            .OverridePropertyName("name");

        StorePlanValidator.Quantity(RuleFor(command => command.QtyDocsInvoice), "qty_docs_invoice");
        StorePlanValidator.Quantity(RuleFor(command => command.QtyDocsPayroll), "qty_docs_payroll");
        StorePlanValidator.Quantity(RuleFor(command => command.QtyDocsRadian), "qty_docs_radian");
        StorePlanValidator.Quantity(RuleFor(command => command.QtyDocsDs), "qty_docs_ds");
        StorePlanValidator.Period(RuleFor(command => command.Period));

        RuleFor(command => command.Observations)
            .MaximumLength(StorePlanValidator.MaxObservationsLength)
            .WithMessage(PlanMessages.MaxLength("observations", StorePlanValidator.MaxObservationsLength))
            .OverridePropertyName("observations");
    }
}

/// <summary>
/// Da al plan el id siguiente al mayor que existe. No usa la identidad de la columna: el guardado por id
/// (<c>PUT plan</c>, contrato heredado) inserta ids elegidos por quien llama sin mover esa secuencia, así que la
/// secuencia podría repetir un id ya usado; además las cantidades necesitan el id antes de guardar. Dos altas
/// simultáneas pueden calcular el mismo id: la segunda choca con la llave primaria al guardar, la transacción se
/// revierte y el endpoint repite la solicitud, que ya ve el plan de la primera y toma el id siguiente.
/// </summary>
internal sealed class CreatePlanHandler(IPlanStore plans, ITenantContext tenant, IClock clock) : IHandler<CreatePlanCommand, PlanSavedResponse>
{
    public async Task<Result<PlanSavedResponse>> HandleAsync(CreatePlanCommand request, CancellationToken ct)
    {
        if (!PlanAccess.IsPlatform(tenant))
        {
            return BillingErrors.PlatformOnly;
        }

        var name = request.Name!.Trim();
        var existing = await plans.ListPlansAsync(null, ct);
        var id = existing.Count == 0 ? 1 : existing.Max(plan => plan.Plan.Id) + 1;
        if (id > StorePlanValidator.MaxId)
        {
            return Error.Validation([new FieldError("id", $"No quedan ids de plan libres: el máximo es {StorePlanValidator.MaxId}.")]);
        }

        if (await plans.IsNameTakenAsync(name, id, ct))
        {
            return Error.Validation([new FieldError("name", PlanMessages.Unique("name"))]);
        }

        var values = new PlanValues(
            (PlanPeriod)request.Period!.Value, request.State ?? true, request.Observations, request.QtyDocsInvoice!.Value,
            request.QtyDocsPayroll!.Value, request.QtyDocsRadian!.Value, request.QtyDocsDs!.Value);
        return new PlanSavedResponse { Plan = await PlanWriter.SaveAsync(plans, null, id, name, values, clock.UtcNow, ct) };
    }
}
