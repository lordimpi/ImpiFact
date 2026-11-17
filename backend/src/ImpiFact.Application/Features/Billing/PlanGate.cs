using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;

namespace ImpiFact.Application.Features.Billing;

/// <summary>
/// Cuota del plan para la emisión y el envío a la DIAN (Arquitectura detallada, sección 7: paso 2, «cuota del plan
/// disponible», y paso 9, «consume cuota»). Las reglas son las de <see cref="PlanRules"/>; los mensajes, los del PHP.
/// </summary>
internal sealed class PlanGate(IPlanStore plans, IClock clock) : IPlanGate
{
    public async Task<Error?> CheckIssueAsync(Guid companyId, DocumentKind kind, CancellationToken ct)
    {
        if (await plans.FindCurrentAsync(companyId, ct) is not { } current)
        {
            return null;
        }

        var plan = current.Plan.Plan;
        return PlanRules.Check(
                plan.IsActive, plan.Period, current.StartsAt, current.EndsAt, current.QuantityOf(kind), current.UsedOf(kind), clock.UtcNow) switch
        {
            PlanAllowance.Allowed => null,
            PlanAllowance.PlanInactive => BillingErrors.PlanInactive,
            PlanAllowance.MonthlyLimitReached => BillingErrors.MonthlyLimitReached,
            PlanAllowance.YearlyLimitReached => BillingErrors.YearlyLimitReached,
            _ => BillingErrors.PackageLimitReached,
        };
    }

    public async Task CountAcceptedAsync(Guid companyId, DocumentKind kind, CancellationToken ct)
    {
        if (await plans.FindCurrentAsync(companyId, ct) is { } current)
        {
            plans.CountUsage(companyId, current.CompanyPlanId, kind);
        }
    }
}
