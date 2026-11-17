using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;

namespace ImpiFact.Application.Features.Billing.StorePlan;

/// <summary>
/// Crea el plan con el id de la solicitud o, si ya existe, lo actualiza (como <c>updateOrCreate</c> del PHP): nombre
/// (se conserva el anterior si no llega), periodo, estado (activo si no llega), observaciones y la cantidad de cada
/// familia en <c>plan_quotas</c>.
/// </summary>
/// <remarks>
/// Dos correcciones al PHP: el nombre único no choca consigo mismo al actualizar (la regla <c>unique</c> del PHP no
/// excluía el plan que se actualiza), y un plan nuevo sin nombre es un error de validación y no un 500.
/// </remarks>
internal sealed class StorePlanHandler(IPlanStore plans, ITenantContext tenant, IClock clock) : IHandler<StorePlanCommand, PlanSavedResponse>
{
    public async Task<Result<PlanSavedResponse>> HandleAsync(StorePlanCommand request, CancellationToken ct)
    {
        if (!PlanAccess.IsPlatform(tenant))
        {
            return BillingErrors.PlatformOnly;
        }

        var id = request.Id!.Value;
        var plan = await plans.FindPlanAsync(id, ct);
        var name = string.IsNullOrWhiteSpace(request.Name) ? plan?.Name : request.Name.Trim();
        if (name is null)
        {
            return Error.Validation([new FieldError("name", PlanMessages.Required("name"))]);
        }

        if (await plans.IsNameTakenAsync(name, id, ct))
        {
            return Error.Validation([new FieldError("name", PlanMessages.Unique("name"))]);
        }

        var now = clock.UtcNow;
        var createdAt = plan?.CreatedAt ?? now;
        if (plan is null)
        {
            plan = new Plan { Id = id, Name = name };
            plans.Add(plan);
        }

        plan.Name = name;
        plan.Period = (PlanPeriod)request.Period!.Value;
        plan.IsActive = request.State ?? true;
        plan.Observations = request.Observations;

        var quotas = (await plans.FindQuotasAsync(id, ct)).ToDictionary(quota => quota.DocumentKind);
        var values = new Dictionary<DocumentKind, long>
        {
            [DocumentKind.Invoice] = request.QtyDocsInvoice!.Value,
            [DocumentKind.Payroll] = request.QtyDocsPayroll!.Value,
            [DocumentKind.Events] = request.QtyDocsRadian!.Value,
            [DocumentKind.SupportDocument] = request.QtyDocsDs!.Value,
        };
        foreach (var (kind, quantity) in values)
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
        return new PlanSavedResponse { Plan = PlanResponse.From(plan, values, createdAt, now) };
    }
}
