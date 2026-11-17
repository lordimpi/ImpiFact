using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Billing;
using ImpiFact.Application.Features.Billing.AssignPlan;
using ImpiFact.Application.Features.Billing.Queries;
using ImpiFact.Application.Features.Billing.StorePlan;

namespace ImpiFact.Api.Plans;

/// <summary>
/// Endpoints del módulo Billing: planes y consumo (carpeta 09 «Planes» de la colección Postman; Arquitectura detallada,
/// sección 9.3). Las rutas heredadas <c>/api/ubl2.1/plan...</c> y las canónicas <c>/api/v1/plans...</c> usan el mismo
/// caso de uso y responden el mismo JSON.
/// </summary>
/// <remarks>
/// En el PHP <c>plan</c>, <c>plan/query</c> y <c>plan/queryusersbyplan</c> eran públicas («php-rules», sección 5.1);
/// aquí son de la plataforma (alcance <c>platform:*</c> sin empresa), como el resto de la administración de planes, y
/// <c>plan/infoplanuser</c>, que en el PHP pedía el token de la empresa, pide <c>documents:read</c>. La asignación de un
/// plan a una empresa es una ruta propia, sin equivalente en el PHP (que lo hacía en el alta de la empresa).
/// </remarks>
internal sealed class PlanEndpoints : IEndpointModule
{
    private const string Tag = "Plans";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var legacy = endpoints.MapGroup("/api/ubl2.1/plan").WithTags(Tag);
        legacy.MapPut("", StorePlan).RequireAuthorization(ApiScopes.Platform);
        legacy.MapGet("/query", (IHandler<QueryPlansQuery, PlansResponse> handler, CancellationToken ct) => QueryPlans(null, handler, ct))
            .RequireAuthorization(ApiScopes.Platform);
        legacy.MapGet("/query/{id:int}", QueryPlans).RequireAuthorization(ApiScopes.Platform);
        legacy.MapGet("/queryusersbyplan/{id:int}", QueryPlanCompanies).RequireAuthorization(ApiScopes.Platform);
        legacy.MapGet("/infoplanuser", CurrentPlan).RequireAuthorization(ApiScopes.DocumentsRead);

        var canonical = endpoints.MapGroup("/api/v1/plans").WithTags(Tag);
        canonical.MapGet("", (IHandler<QueryPlansQuery, PlansResponse> handler, CancellationToken ct) => QueryPlans(null, handler, ct))
            .RequireAuthorization(ApiScopes.Platform);
        canonical.MapGet("/{id:int}", QueryPlans).RequireAuthorization(ApiScopes.Platform);
        canonical.MapPut("/{id:int}", (int id, StorePlanCommand body, IHandler<StorePlanCommand, PlanSavedResponse> handler, CancellationToken ct) =>
            StorePlan(body with { Id = id }, handler, ct)).RequireAuthorization(ApiScopes.Platform);
        canonical.MapGet("/{id:int}/companies", QueryPlanCompanies).RequireAuthorization(ApiScopes.Platform);
        canonical.MapGet("/current", CurrentPlan).RequireAuthorization(ApiScopes.DocumentsRead);

        endpoints.MapPut("/api/v1/companies/{nit}/plan", AssignPlan).WithTags(Tag).RequireAuthorization(ApiScopes.Platform);
    }

    private static async Task<IResult> StorePlan(StorePlanCommand body, IHandler<StorePlanCommand, PlanSavedResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body, ct)).ToHttpResult();

    private static async Task<IResult> QueryPlans(int? id, IHandler<QueryPlansQuery, PlansResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new QueryPlansQuery(id), ct)).ToHttpResult();

    private static async Task<IResult> QueryPlanCompanies(
        int id, IHandler<QueryPlanCompaniesQuery, PlanCompaniesResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new QueryPlanCompaniesQuery(id), ct)).ToHttpResult();

    private static async Task<IResult> CurrentPlan(IHandler<GetCurrentPlanQuery, CurrentPlanResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetCurrentPlanQuery(), ct)).ToHttpResult();

    private static async Task<IResult> AssignPlan(
        string nit, AssignPlanCommand body, IHandler<AssignPlanCommand, PlanAssignedResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body with { Nit = nit }, ct)).ToHttpResult();
}
