using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Billing;

namespace ImpiFact.Application.Features.Billing.AssignPlan;

/// <summary>
/// Asigna un plan a una empresa (<c>PUT /api/v1/companies/{nit}/plan</c>; caso de uso <c>AssignPlan</c> de la
/// Arquitectura detallada, sección 3). En el PHP se asignaba con <c>type_plan_id</c> en el alta de la empresa; aquí es
/// una ruta propia de la plataforma.
/// </summary>
public sealed record AssignPlanCommand : ICommand, ICompanyOptional
{
    [JsonIgnore]
    public string? Nit { get; init; }

    [JsonPropertyName("plan_id")]
    public int? PlanId { get; init; }
}

public sealed class AssignPlanValidator : AbstractValidator<AssignPlanCommand>
{
    public AssignPlanValidator() =>
        RuleFor(command => command.PlanId)
            .NotNull().WithMessage(PlanMessages.Required("plan_id"))
            .OverridePropertyName("plan_id");
}

/// <summary>
/// El plan contratado nuevo empieza ahora y queda activo; los que la empresa tenía activos se desactivan. Como en el
/// PHP, que ponía <c>start_plan_date</c> en la hora del alta, volver a asignar el mismo plan lo renueva: el periodo
/// empieza de nuevo y el consumo arranca en cero, porque se cuenta por plan contratado.
/// </summary>
internal sealed class AssignPlanHandler(IPlanStore plans, ITenantContext tenant, IClock clock) : IHandler<AssignPlanCommand, PlanAssignedResponse>
{
    public async Task<Result<PlanAssignedResponse>> HandleAsync(AssignPlanCommand request, CancellationToken ct)
    {
        if (!PlanAccess.IsPlatform(tenant))
        {
            return BillingErrors.PlatformOnly;
        }

        var planId = request.PlanId!.Value;
        if ((await plans.ListPlansAsync(planId, ct)) is not [var plan])
        {
            return BillingErrors.PlanNotFound(planId);
        }

        if (await plans.FindCompanyIdAsync(request.Nit ?? "", ct) is not { } companyId)
        {
            return BillingErrors.CompanyNotFound;
        }

        foreach (var active in await plans.FindActiveAssignmentsAsync(companyId, ct))
        {
            active.IsActive = false;
        }

        var now = clock.UtcNow;
        plans.Add(new CompanyPlan { CompanyId = companyId, PlanId = planId, StartsAt = now, IsActive = true });
        return new PlanAssignedResponse { Plan = PlanResponse.From(plan), StartPlanDate = PlanDates.Format(now) };
    }
}
