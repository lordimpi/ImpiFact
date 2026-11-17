using ImpiFact.Application.Abstractions.Handlers;

namespace ImpiFact.Application.Features.Billing.Queries;

/// <summary>
/// Planes de la plataforma (<c>GET plan/query/{id?}</c>, <c>ConfigurationController::queryPlan</c> del PHP): uno por
/// su id o todos. Operación de plataforma.
/// </summary>
public sealed record QueryPlansQuery(int? Id) : ICompanyOptional;

/// <summary>
/// Empresas con el plan contratado activo (<c>GET plan/queryusersbyplan/{id}</c>,
/// <c>ConfigurationController::queryUsersByPlan</c> del PHP). Operación de plataforma.
/// </summary>
public sealed record QueryPlanCompaniesQuery(int Id) : ICompanyOptional;

/// <summary>
/// Plan vigente de la empresa de la credencial con los documentos que le quedan (<c>GET plan/infoplanuser</c>,
/// <c>ConfigurationController::infoPlanUser</c> del PHP).
/// </summary>
public sealed record GetCurrentPlanQuery;
