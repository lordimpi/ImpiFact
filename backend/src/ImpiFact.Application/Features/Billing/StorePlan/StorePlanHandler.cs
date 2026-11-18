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

        var values = new PlanValues(
            (PlanPeriod)request.Period!.Value, request.State ?? true, request.Observations, request.QtyDocsInvoice!.Value,
            request.QtyDocsPayroll!.Value, request.QtyDocsRadian!.Value, request.QtyDocsDs!.Value);
        return new PlanSavedResponse { Plan = await PlanWriter.SaveAsync(plans, plan, id, name, values, clock.UtcNow, ct) };
    }
}
