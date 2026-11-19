using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;

namespace ImpiFact.Application.Features.Billing;

/// <summary>Campos de un plan que escriben el alta (<c>createPlan</c>) y el guardado por id (<c>PUT plan</c>).</summary>
internal sealed record PlanValues(
    PlanPeriod Period, bool IsActive, string? Observations, long Invoice, long Payroll, long Events, long SupportDocument);

/// <summary>Escribe un plan y sus cuatro cantidades (<c>plan_quotas</c>), sea nuevo o ya existente.</summary>
internal static class PlanWriter
{
    /// <param name="plan">El plan ya cargado con rastreo, o nulo si hay que crearlo con <paramref name="id"/>.</param>
    public static async Task<PlanResponse> SaveAsync(
        IPlanStore plans, Plan? plan, int id, string name, PlanValues values, DateTimeOffset now, CancellationToken ct)
    {
        var createdAt = plan?.CreatedAt ?? now;
        if (plan is null)
        {
            plan = new Plan { Id = id, Name = name };
            plans.Add(plan);
        }

        plan.Name = name;
        plan.Period = values.Period;
        plan.IsActive = values.IsActive;
        plan.Observations = values.Observations;

        var quotas = (await plans.FindQuotasAsync(id, ct)).ToDictionary(quota => quota.DocumentKind);
        var quantities = new Dictionary<DocumentKind, long>
        {
            [DocumentKind.Invoice] = values.Invoice,
            [DocumentKind.Payroll] = values.Payroll,
            [DocumentKind.Events] = values.Events,
            [DocumentKind.SupportDocument] = values.SupportDocument,
        };
        foreach (var (kind, quantity) in quantities)
        {
            if (quotas.TryGetValue(kind, out var quota))
            {
                quota.Quantity = quantity;
            }
            else
            {
                plans.Add(new PlanQuota { PlanId = id, DocumentKind = kind, Quantity = quantity });
            }
        }

        // El guardado pone las fechas con el mismo reloj al final de la solicitud; la respuesta las anticipa.
        return PlanResponse.From(plan, quantities, createdAt, now);
    }
}
