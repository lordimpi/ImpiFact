using System.Globalization;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;

namespace ImpiFact.Application.Features.Billing.Queries;

/// <summary>
/// Un plan por id o todos (los de id mayor que 0, como el PHP). Un id que no existe es 404; el PHP respondía un 500 con
/// el texto de la excepción de Laravel.
/// </summary>
internal sealed class QueryPlansHandler(IPlanStore plans, ITenantContext tenant) : IHandler<QueryPlansQuery, PlansResponse>
{
    public async Task<Result<PlansResponse>> HandleAsync(QueryPlansQuery request, CancellationToken ct)
    {
        if (!PlanAccess.IsPlatform(tenant))
        {
            return BillingErrors.PlatformOnly;
        }

        var found = await plans.ListPlansAsync(request.Id, ct);
        if (request.Id is { } id)
        {
            return found is [var plan] ? new PlansResponse { Data = PlanResponse.From(plan) } : BillingErrors.PlanNotFound(id);
        }

        return new PlansResponse { Data = found.Select(PlanResponse.From).ToList() };
    }
}

/// <summary>Empresas con el plan contratado activo, con el mensaje del PHP.</summary>
internal sealed class QueryPlanCompaniesHandler(IPlanStore plans, ITenantContext tenant)
    : IHandler<QueryPlanCompaniesQuery, PlanCompaniesResponse>
{
    public async Task<Result<PlanCompaniesResponse>> HandleAsync(QueryPlanCompaniesQuery request, CancellationToken ct)
    {
        if (!PlanAccess.IsPlatform(tenant))
        {
            return BillingErrors.PlatformOnly;
        }

        if (await plans.FindPlanAsync(request.Id, ct) is null)
        {
            return BillingErrors.PlanNotFound(request.Id);
        }

        var subscribers = await plans.ListSubscribersAsync(request.Id, ct);
        return new PlanCompaniesResponse
        {
            Message = "Usuarios registrados al plan ID: " + request.Id.ToString(CultureInfo.InvariantCulture),
            User = subscribers
                .Select(company => new PlanCompanyResponse
                {
                    IdentificationNumber = company.IdentificationNumber,
                    Dv = company.Dv,
                    Name = company.Name,
                    Email = company.Email,
                    State = company.IsActive,
                    TypePlanId = request.Id,
                    StartPlanDate = PlanDates.Format(company.StartsAt),
                })
                .ToList(),
        };
    }
}

/// <summary>
/// Plan vigente de la empresa de la credencial (<c>infoPlanUser</c> del PHP): periodo con su nombre, inicio, fecha de
/// renovación (fin del periodo; nula si el plan no vence) y documentos que quedan por familia. Sin plan contratado
/// responde como el plan por defecto del PHP: «SIN LIMITES - NO PLAN», sin fechas y 0 documentos restantes.
/// </summary>
internal sealed class GetCurrentPlanHandler(IPlanStore plans, ITenantContext tenant) : IHandler<GetCurrentPlanQuery, CurrentPlanResponse>
{
    public async Task<Result<CurrentPlanResponse>> HandleAsync(GetCurrentPlanQuery request, CancellationToken ct)
    {
        var companyId = tenant.CompanyId ?? throw new InvalidOperationException("The tenant decorator guarantees a company.");
        var nit = await plans.FindCompanyNitAsync(companyId, ct) ?? "";
        var current = await plans.FindCurrentAsync(companyId, ct);
        var plan = current is null ? null : PlanResponse.From(current.Plan);
        var period = current?.Plan.Plan.Period;
        var periodName = PlanDates.PeriodName(period);
        var startDate = current is null ? null : PlanDates.Format(current.StartsAt);
        var renovation = current is null ? null : PlanRules.PeriodEnd(current.Plan.Plan.Period, current.StartsAt, current.EndsAt);
        var renovationDate = renovation is { } end ? PlanDates.Format(end) : null;
        return new CurrentPlanResponse
        {
            Message = "Informacion del plan vigente para el usuario: " + nit,
            Plan = plan,
            Period = periodName,
            StartPlanDate = startDate,
            RenovationDate = renovationDate,
            Plan2 = plan,
            Period2 = periodName,
            StartPlanDate2 = startDate,
            RenovationDate2 = renovationDate,
            Plan3 = plan,
            Period3 = periodName,
            StartPlanDate3 = startDate,
            RenovationDate3 = renovationDate,
            Plan4 = plan,
            Period4 = periodName,
            StartPlanDate4 = startDate,
            RenovationDate4 = renovationDate,
            DocsLeftInvoice = Left(current, DocumentKind.Invoice),
            DocsLeftPayroll = Left(current, DocumentKind.Payroll),
            DocsLeftRadian = Left(current, DocumentKind.Events),
            DocsLeftDs = Left(current, DocumentKind.SupportDocument),
        };
    }

    private static long Left(CurrentPlan? current, DocumentKind kind) =>
        current is null ? 0 : current.QuantityOf(kind) - current.UsedOf(kind);
}
