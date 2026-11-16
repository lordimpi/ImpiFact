using ImpiFact.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImpiFact.Infrastructure.Persistence.Configurations.Billing;

// Planes y consumo (Modelo de datos detallado, sección 3.6). No hay planes sembrados: el modelo no define sus
// nombres ni sus cuotas, y la plataforma los crea desde el panel.

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans");
        builder.Property(plan => plan.Name).HasMaxLength(100);
        builder.Property(plan => plan.Period).HasStoreDefault(PlanPeriod.Default);
        builder.Property(plan => plan.Observations).HasMaxLength(500);
        builder.HasIndex(plan => plan.Name).IsUnique();
    }
}

internal sealed class PlanQuotaConfiguration : IEntityTypeConfiguration<PlanQuota>
{
    public void Configure(EntityTypeBuilder<PlanQuota> builder)
    {
        builder.ToTable("plan_quotas");
        builder.HasKey(quota => new { quota.PlanId, quota.DocumentKind });
        builder.References<PlanQuota, Plan>(quota => quota.PlanId);
    }
}

internal sealed class CompanyPlanConfiguration : IEntityTypeConfiguration<CompanyPlan>
{
    public void Configure(EntityTypeBuilder<CompanyPlan> builder)
    {
        builder.ToTable("company_plans");
        builder.BelongsToCompany();
        builder.References<CompanyPlan, Plan>(companyPlan => companyPlan.PlanId);
        builder.HasIndex(companyPlan => new { companyPlan.CompanyId, companyPlan.IsActive });
    }
}

internal sealed class PlanUsageConfiguration : IEntityTypeConfiguration<PlanUsage>
{
    public void Configure(EntityTypeBuilder<PlanUsage> builder)
    {
        builder.ToTable("plan_usage");
        builder.HasKey(usage => new { usage.CompanyPlanId, usage.DocumentKind });
        builder.Property(usage => usage.Used).HasStoreDefault(0L);
        builder.References<PlanUsage, CompanyPlan>(usage => usage.CompanyPlanId);
    }
}
