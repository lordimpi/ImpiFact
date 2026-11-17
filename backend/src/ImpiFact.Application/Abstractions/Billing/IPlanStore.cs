using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;

namespace ImpiFact.Application.Abstractions.Billing;

/// <summary>
/// Persistencia del módulo Billing (planes y consumo; Modelo de datos detallado, sección 3.6): <c>plans</c>,
/// <c>plan_quotas</c>, <c>company_plans</c> y <c>plan_usage</c>, más los pocos datos de la empresa que muestran sus
/// respuestas. Las filas agregadas o cambiadas se guardan con el próximo guardado de la unidad de trabajo.
/// </summary>
/// <remarks>
/// Los planes son de la plataforma (sin <c>company_id</c>); los planes contratados son de cada empresa y, como en los
/// demás puertos con <c>companyId</c>, solo se leen y escriben los de esa empresa.
/// </remarks>
public interface IPlanStore
{
    /// <summary>El plan con rastreo de cambios; nulo si no existe.</summary>
    Task<Plan?> FindPlanAsync(int planId, CancellationToken ct);

    /// <summary>Las cantidades por familia del plan, con rastreo de cambios.</summary>
    Task<IReadOnlyList<PlanQuota>> FindQuotasAsync(int planId, CancellationToken ct);

    /// <summary>Verdadero si otro plan (distinto de <paramref name="exceptPlanId"/>) ya usa ese nombre.</summary>
    Task<bool> IsNameTakenAsync(string name, int exceptPlanId, CancellationToken ct);

    void Add(Plan plan);

    void Add(PlanQuota quota);

    /// <summary>
    /// Planes con sus cantidades, sin rastreo y ordenados por id: solo <paramref name="planId"/> si viene, o todos los de
    /// id mayor que 0 (como el PHP, que deja fuera su plan por defecto 0).
    /// </summary>
    Task<IReadOnlyList<PlanWithQuotas>> ListPlansAsync(int? planId, CancellationToken ct);

    /// <summary>Plan vigente de la empresa (el contratado activo más reciente); nulo si no tiene.</summary>
    Task<CurrentPlan?> FindCurrentAsync(Guid companyId, CancellationToken ct);

    /// <summary>
    /// Suma un documento de la familia <paramref name="kind"/> a <c>plan_usage</c> del plan contratado. No lee ni
    /// rastrea la fila: la suma es un solo <c>used = used + 1</c> atómico que se ejecuta en la misma transacción del
    /// próximo guardado, después de los demás cambios, así dos procesos que suman a la vez nunca pierden una cuenta.
    /// </summary>
    /// <exception cref="InvalidOperationException">Si la solicitud es de otra empresa.</exception>
    void CountUsage(Guid companyId, Guid companyPlanId, DocumentKind kind);

    /// <summary>Planes contratados activos de la empresa, con rastreo de cambios.</summary>
    Task<IReadOnlyList<CompanyPlan>> FindActiveAssignmentsAsync(Guid companyId, CancellationToken ct);

    /// <exception cref="InvalidOperationException">Si la solicitud es de otra empresa.</exception>
    void Add(CompanyPlan companyPlan);

    /// <summary>Empresas con el plan contratado activo, ordenadas por NIT.</summary>
    Task<IReadOnlyList<PlanSubscriber>> ListSubscribersAsync(int planId, CancellationToken ct);

    /// <summary>Id de la empresa con ese NIT (Número de Identificación Tributaria); nulo si no existe.</summary>
    Task<Guid?> FindCompanyIdAsync(string nit, CancellationToken ct);

    /// <summary>NIT de la empresa; nulo si no existe.</summary>
    Task<string?> FindCompanyNitAsync(Guid companyId, CancellationToken ct);
}

/// <summary>Un plan con la cantidad de documentos que incluye cada familia.</summary>
public sealed record PlanWithQuotas(Plan Plan, IReadOnlyDictionary<DocumentKind, long> Quotas);

/// <summary>El plan vigente de una empresa con lo que lleva usado.</summary>
/// <param name="CompanyPlanId">Plan contratado (<c>company_plans.id</c>), al que se suma el consumo.</param>
/// <param name="Used">Documentos aceptados por familia en este plan contratado; una familia sin fila no ha usado ninguno.</param>
public sealed record CurrentPlan(
    Guid CompanyPlanId,
    PlanWithQuotas Plan,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    IReadOnlyDictionary<DocumentKind, long> Used)
{
    public long QuantityOf(DocumentKind kind) => Plan.Quotas.GetValueOrDefault(kind);

    public long UsedOf(DocumentKind kind) => Used.GetValueOrDefault(kind);
}

/// <summary>Empresa con un plan contratado activo.</summary>
/// <param name="Name">Razón social.</param>
/// <param name="StartsAt">Inicio del plan contratado.</param>
public sealed record PlanSubscriber(string IdentificationNumber, string Dv, string Name, string Email, bool IsActive, DateTimeOffset StartsAt);
