using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Billing;

/// <summary>
/// Planes y consumo sobre el contexto de EF Core (Entity Framework, el mapeador de objetos a tablas) de la solicitud.
/// Las lecturas de una empresa quitan el filtro global de tenant y ponen la empresa en el WHERE, como los demás
/// adaptadores con <c>companyId</c>, así sirven también a la plataforma (sin empresa en el contexto). El consumo no
/// pasa por EF: queda pendiente en <see cref="PendingPlanUsage"/> hasta el próximo guardado.
/// </summary>
internal sealed class EfPlanStore(ImpiFactDbContext db, PendingPlanUsage pendingUsage, ITenantContext tenant) : IPlanStore
{
    public Task<Plan?> FindPlanAsync(int planId, CancellationToken ct) =>
        db.Set<Plan>().FirstOrDefaultAsync(plan => plan.Id == planId, ct);

    public async Task<IReadOnlyList<PlanQuota>> FindQuotasAsync(int planId, CancellationToken ct) =>
        await db.Set<PlanQuota>().Where(quota => quota.PlanId == planId).ToListAsync(ct);

    public Task<bool> IsNameTakenAsync(string name, int exceptPlanId, CancellationToken ct) =>
        db.Set<Plan>().AnyAsync(plan => plan.Name == name && plan.Id != exceptPlanId, ct);

    public void Add(Plan plan) => db.Set<Plan>().Add(plan);

    public void Add(PlanQuota quota) => db.Set<PlanQuota>().Add(quota);

    public async Task<IReadOnlyList<PlanWithQuotas>> ListPlansAsync(int? planId, CancellationToken ct)
    {
        var plans = await db.Set<Plan>().AsNoTracking()
            .Where(plan => planId == null ? plan.Id > 0 : plan.Id == planId)
            .OrderBy(plan => plan.Id)
            .ToListAsync(ct);
        var ids = plans.Select(plan => plan.Id).ToList();
        var quotas = await db.Set<PlanQuota>().AsNoTracking().Where(quota => ids.Contains(quota.PlanId)).ToListAsync(ct);
        return plans
            .Select(plan => new PlanWithQuotas(
                plan, quotas.Where(quota => quota.PlanId == plan.Id).ToDictionary(quota => quota.DocumentKind, quota => quota.Quantity)))
            .ToList();
    }

    public async Task<CurrentPlan?> FindCurrentAsync(Guid companyId, CancellationToken ct)
    {
        var assignment = await CompanyPlans()
            .AsNoTracking()
            .Where(companyPlan => companyPlan.CompanyId == companyId && companyPlan.IsActive)
            .OrderByDescending(companyPlan => companyPlan.StartsAt)
            .ThenByDescending(companyPlan => companyPlan.Id)
            .FirstOrDefaultAsync(ct);
        if (assignment is null)
        {
            return null;
        }

        var plan = (await ListPlansAsync(assignment.PlanId, ct)).Single();
        var used = await db.Set<PlanUsage>().AsNoTracking()
            .Where(usage => usage.CompanyPlanId == assignment.Id)
            .ToDictionaryAsync(usage => usage.DocumentKind, usage => usage.Used, ct);
        return new CurrentPlan(assignment.Id, plan, assignment.StartsAt, assignment.EndsAt, used);
    }

    public void CountUsage(Guid companyId, Guid companyPlanId, DocumentKind kind)
    {
        TenantWriteCheck.EnsureCanWrite(tenant, companyId, "Plan usage");
        pendingUsage.Add(companyPlanId, kind);
    }

    public async Task<IReadOnlyList<CompanyPlan>> FindActiveAssignmentsAsync(Guid companyId, CancellationToken ct) =>
        await CompanyPlans().Where(companyPlan => companyPlan.CompanyId == companyId && companyPlan.IsActive).ToListAsync(ct);

    public void Add(CompanyPlan companyPlan)
    {
        ArgumentNullException.ThrowIfNull(companyPlan);
        TenantWriteCheck.EnsureCanWrite(tenant, companyPlan.CompanyId, nameof(CompanyPlan));
        db.Set<CompanyPlan>().Add(companyPlan);
    }

    public async Task<IReadOnlyList<PlanSubscriber>> ListSubscribersAsync(int planId, CancellationToken ct) =>
        await (
            from companyPlan in CompanyPlans().AsNoTracking()
            where companyPlan.PlanId == planId && companyPlan.IsActive
            join company in db.Set<Company>().AsNoTracking() on companyPlan.CompanyId equals company.Id
            orderby company.IdentificationNumber
            select new PlanSubscriber(company.IdentificationNumber, company.Dv, company.Name, company.Email, company.IsActive, companyPlan.StartsAt))
            .ToListAsync(ct);

    public Task<Guid?> FindCompanyIdAsync(string nit, CancellationToken ct) =>
        db.Set<Company>().AsNoTracking().Where(company => company.IdentificationNumber == nit).Select(company => (Guid?)company.Id)
            .FirstOrDefaultAsync(ct);

    public Task<string?> FindCompanyNitAsync(Guid companyId, CancellationToken ct) =>
        db.Set<Company>().AsNoTracking().Where(company => company.Id == companyId).Select(company => company.IdentificationNumber)
            .FirstOrDefaultAsync(ct);

    private IQueryable<CompanyPlan> CompanyPlans() => db.Set<CompanyPlan>().IgnoreQueryFilters([QueryFilters.Tenant]);
}
