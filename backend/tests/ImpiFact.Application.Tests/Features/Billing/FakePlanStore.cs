using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;

namespace ImpiFact.Application.Tests.Features.Billing;

/// <summary>Almacén de planes en memoria para las pruebas de los casos de uso de Billing.</summary>
public sealed class FakePlanStore : IPlanStore
{
    public CurrentPlan? Current { get; set; }

    public List<(Guid CompanyId, Guid CompanyPlanId, DocumentKind Kind)> Counted { get; } = [];

    public Task<Plan?> FindPlanAsync(int planId, CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyList<PlanQuota>> FindQuotasAsync(int planId, CancellationToken ct) => throw new NotSupportedException();

    public Task<bool> IsNameTakenAsync(string name, int exceptPlanId, CancellationToken ct) => throw new NotSupportedException();

    public void Add(Plan plan) => throw new NotSupportedException();

    public void Add(PlanQuota quota) => throw new NotSupportedException();

    public Task<IReadOnlyList<PlanWithQuotas>> ListPlansAsync(int? planId, CancellationToken ct) => throw new NotSupportedException();

    public Task<CurrentPlan?> FindCurrentAsync(Guid companyId, CancellationToken ct) => Task.FromResult(Current);

    public void CountUsage(Guid companyId, Guid companyPlanId, DocumentKind kind) => Counted.Add((companyId, companyPlanId, kind));

    public Task<IReadOnlyList<CompanyPlan>> FindActiveAssignmentsAsync(Guid companyId, CancellationToken ct) => throw new NotSupportedException();

    public void Add(CompanyPlan companyPlan) => throw new NotSupportedException();

    public Task<IReadOnlyList<PlanSubscriber>> ListSubscribersAsync(int planId, CancellationToken ct) => throw new NotSupportedException();

    public Task<Guid?> FindCompanyIdAsync(string nit, CancellationToken ct) => throw new NotSupportedException();

    public Task<string?> FindCompanyNitAsync(Guid companyId, CancellationToken ct) => throw new NotSupportedException();
}
