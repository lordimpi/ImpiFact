using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
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
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Contrato heredado del PHP: sigue activo para la colección Postman, fuera de la documentación; se borra en la fase 7.
        var legacy = endpoints.MapGroup("/api/ubl2.1/plan").WithTags(ApiTags.Plans).ExcludeFromDescription();
        legacy.MapPut("", StorePlan).RequireAuthorization(ApiScopes.Platform);
        legacy.MapGet("/query", (IHandler<QueryPlansQuery, PlansResponse> handler, CancellationToken ct) => QueryPlans(null, handler, ct))
            .RequireAuthorization(ApiScopes.Platform);
        legacy.MapGet("/query/{id:int}", QueryPlans).RequireAuthorization(ApiScopes.Platform);
        legacy.MapGet("/queryusersbyplan/{id:int}", QueryPlanCompanies).RequireAuthorization(ApiScopes.Platform);
        legacy.MapGet("/infoplanuser", CurrentPlan).RequireAuthorization(ApiScopes.DocumentsRead);

        var canonical = endpoints.MapGroup("/api/v1/plans").WithTags(ApiTags.Plans);
        canonical.MapGet("", (IHandler<QueryPlansQuery, PlansResponse> handler, CancellationToken ct) => QueryPlans(null, handler, ct))
            .RequireAuthorization(ApiScopes.Platform)
            .Produces<PlansResponse>()
            .WithSummary("Listar los planes")
            .WithDescription("Lista todos los planes de la plataforma en `data`, con la cantidad de documentos por familia " +
                "(`qty_docs_invoice`, `qty_docs_payroll`, `qty_docs_radian`, `qty_docs_ds`), el periodo y el estado.");
        canonical.MapGet("/{id:int}", QueryPlans).RequireAuthorization(ApiScopes.Platform)
            .Produces<PlansResponse>()
            .WithSummary("Ver un plan")
            .WithDescription("Responde en `data` el plan con ese `id`. Si no existe: 404 (`plan.not_found`).");
        canonical.MapPut("/{id:int}", (int id, StorePlanCommand body, IHandler<StorePlanCommand, PlanSavedResponse> handler, CancellationToken ct) =>
            StorePlan(body with { Id = id }, handler, ct)).RequireAuthorization(ApiScopes.Platform)
            .Produces<PlanSavedResponse>()
            .WithRequestExamples(ExampleBodies.Plan)
            .WithSummary("Crear o actualizar un plan")
            .WithDescription(
                "Crea el plan con ese `id` o lo actualiza si ya existe. Campos: `name` (único; obligatorio al crear), la " +
                "cantidad de documentos por familia (`qty_docs_invoice` para facturas y notas, `qty_docs_payroll`, " +
                "`qty_docs_radian`, `qty_docs_ds`), `period` (1 mensual, 2 anual, 3 paquete), `state` (activo si no llega) " +
                "y `observations`. Los números pueden llegar como número o como texto. Nombre repetido: 422.");
        canonical.MapGet("/{id:int}/companies", QueryPlanCompanies).RequireAuthorization(ApiScopes.Platform)
            .Produces<PlanCompaniesResponse>()
            .WithSummary("Empresas con un plan")
            .WithDescription("Lista las empresas que tienen activo el plan con ese `id`. Si el plan no existe: 404 (`plan.not_found`).");
        canonical.MapGet("/current", CurrentPlan).RequireAuthorization(ApiScopes.DocumentsRead)
            .Produces<CurrentPlanResponse>()
            .WithSummary("Plan vigente de la empresa")
            .WithDescription(
                "Plan vigente de la empresa de la credencial con los documentos que le quedan en el periodo. El consumo sube " +
                "cuando la DIAN acepta un documento; al llegar al límite, la emisión responde 403 (`plan.limit_reached`).");

        endpoints.MapPut("/api/v1/companies/{nit}/plan", AssignPlan).WithTags(ApiTags.Plans).RequireAuthorization(ApiScopes.Platform)
            .Produces<PlanAssignedResponse>()
            .WithRequestExamples(ExampleBodies.AssignPlan)
            .WithSummary("Asignar un plan a una empresa")
            .WithDescription(
                "Asigna a la empresa del `{nit}` el plan `plan_id`. El plan empieza ahora y queda activo; los que la empresa " +
                "tenía activos se desactivan. Volver a asignar el mismo plan lo renueva: el periodo empieza de nuevo y el " +
                "consumo vuelve a cero. Plan o empresa que no existen: 404 (`plan.not_found`, `company.not_found`).");
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
