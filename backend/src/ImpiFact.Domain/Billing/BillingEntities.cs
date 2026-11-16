using ImpiFact.Domain.Common;

namespace ImpiFact.Domain.Billing;

// Planes y consumo (Modelo de datos detallado, sección 3.6). El consumo vive solo en plan_usage: no se duplica
// en la empresa como hacía el PHP con absolut_plan_documents.

/// <summary>Plan de la plataforma (<c>plans</c>; en el PHP, <c>plans</c> y <c>type_plans</c>). La base genera su id.</summary>
public sealed class Plan : IAuditable, IVersioned
{
    public int Id { get; init; }

    public required string Name { get; set; }

    public PlanPeriod Period { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Observations { get; set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long RowVersion { get; private set; }
}

/// <summary>Periodo de un plan.</summary>
public enum PlanPeriod : short
{
    /// <summary>Valor por defecto del PHP, sin periodo.</summary>
    Default = 0,
    Monthly = 1,
    Yearly = 2,

    /// <summary>Paquete de documentos sin vencimiento.</summary>
    Package = 3,
}

/// <summary>Cantidad de documentos de una familia que incluye un plan (<c>plan_quotas</c>).</summary>
public sealed class PlanQuota
{
    public int PlanId { get; init; }

    public DocumentKind DocumentKind { get; init; }

    public long Quantity { get; set; }
}

/// <summary>Plan contratado por una empresa (<c>company_plans</c>); reemplaza los cuatro planes en columnas del PHP.</summary>
public sealed class CompanyPlan : TenantEntity
{
    public int PlanId { get; init; }

    public DateTimeOffset StartsAt { get; init; }

    public DateTimeOffset? EndsAt { get; set; }

    public bool IsActive { get; set; }
}

/// <summary>
/// Documentos consumidos de un plan contratado por familia (<c>plan_usage</c>). Se incrementa al aceptar un
/// documento; la versión de fila evita que dos aceptaciones simultáneas pierdan una cuenta.
/// </summary>
public sealed class PlanUsage : IVersioned
{
    public Guid CompanyPlanId { get; init; }

    public DocumentKind DocumentKind { get; init; }

    public long Used { get; set; }

    public long RowVersion { get; private set; }
}
