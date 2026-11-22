using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Billing;
using ImpiFact.Application.Features.Billing.AssignPlan;
using ImpiFact.Application.Features.Billing.CreatePlan;
using ImpiFact.Application.Features.Billing.Queries;
using ImpiFact.Application.Features.Billing.StorePlan;

namespace ImpiFact.Api.Plans;

/// <summary>
/// Endpoints del módulo Billing: planes y consumo (carpeta 09 «Planes» de la colección Postman; Arquitectura detallada,
/// sección 9.3), bajo <c>/api/v1/plans...</c>. Las heredadas <c>/api/ubl2.1/plan...</c> se borraron en la fase 7a (T9b2).
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
        var canonical = endpoints.MapGroup("/api/v1/plans").WithTags(ApiTags.Plans);
        canonical.MapGet("", (IHandler<QueryPlansQuery, PlansResponse> handler, CancellationToken ct) => QueryPlans(null, handler, ct))
            .RequireAuthorization(ApiScopes.Platform)
            .Produces<PlansResponse>()
            .WithOperationId("listPlans")
            .WithSummary("Listar los planes")
            .WithDescription("Lista todos los planes de la plataforma en `data`, con la cantidad de documentos por familia " +
                "(`qty_docs_invoice`, `qty_docs_payroll`, `qty_docs_radian`, `qty_docs_ds`), el periodo y el estado.");
        canonical.MapGet("/{id:int}", QueryPlans).RequireAuthorization(ApiScopes.Platform)
            .Produces<PlansResponse>()
            .WithOperationId("getPlan")
            .WithSummary("Ver un plan")
            .WithDescription("Responde en `data` el plan con ese `id`. Si no existe: 404 (`plan.not_found`).");
        canonical.MapPost("", CreatePlan).RequireAuthorization(ApiScopes.Platform)
            .Produces<PlanSavedResponse>(StatusCodes.Status201Created)
            .WithRequestExamples(ExampleBodies.Plan)
            .WithOperationId("createPlan")
            .WithSummary("Crear un plan")
            .WithDescription(
                "Crea un plan nuevo y le asigna el `id` siguiente al mayor que existe (un `id` en el cuerpo se ignora; nunca " +
                "reemplaza un plan). Responde 201 con el plan en `plan` y su dirección en `Location`. Campos: `name` (único y " +
                "obligatorio), la cantidad de documentos que la empresa puede emitir en cada periodo por familia " +
                "(`qty_docs_invoice` para facturas y notas, `qty_docs_payroll` para nómina, `qty_docs_radian` para eventos " +
                "RADIAN y `qty_docs_ds` para documentos soporte; 0 no deja emitir esa familia), `period` (1 mensual, 2 anual, " +
                "3 paquete sin vencimiento), `state` (activo si no llega) y `observations`. Nombre repetido o campos que " +
                "faltan: 422. Para cambiar un plan existente se usa `PUT /api/v1/plans/{id}`.");
        canonical.MapPut("/{id:int}", (int id, StorePlanCommand body, IHandler<StorePlanCommand, PlanSavedResponse> handler, CancellationToken ct) =>
            StorePlan(body with { Id = id }, handler, ct)).RequireAuthorization(ApiScopes.Platform)
            .Produces<PlanSavedResponse>()
            .WithRequestExamples(ExampleBodies.Plan)
            .WithOperationId("savePlan")
            .WithSummary("Crear o actualizar un plan")
            .WithDescription(
                "Crea el plan con ese `id` o lo actualiza si ya existe; para crear uno sin elegir el id está `POST " +
                "/api/v1/plans`. Campos: `name` (único; obligatorio al crear), la " +
                "cantidad de documentos por familia (`qty_docs_invoice` para facturas y notas, `qty_docs_payroll`, " +
                "`qty_docs_radian`, `qty_docs_ds`), `period` (1 mensual, 2 anual, 3 paquete), `state` (activo si no llega) " +
                "y `observations`. Los números pueden llegar como número o como texto. Nombre repetido: 422.");
        canonical.MapGet("/{id:int}/companies", QueryPlanCompanies).RequireAuthorization(ApiScopes.Platform)
            .Produces<PlanCompaniesResponse>()
            .WithOperationId("listPlanCompanies")
            .WithSummary("Empresas con un plan")
            .WithDescription("Lista las empresas que tienen activo el plan con ese `id`. Si el plan no existe: 404 (`plan.not_found`).");
        canonical.MapGet("/current", CurrentPlan).RequireAuthorization(ApiScopes.DocumentsRead)
            .Produces<CurrentPlanResponse>()
            .WithOperationId("getCurrentPlan")
            .WithSummary("Plan vigente de la empresa")
            .WithDescription(
                "Plan vigente de la empresa de la credencial con los documentos que le quedan en el periodo. El consumo sube " +
                "cuando la DIAN acepta un documento; al llegar al límite, la emisión responde 403 (`plan.limit_reached`).");

        endpoints.MapPut("/api/v1/companies/{nit}/plan", AssignPlan).WithTags(ApiTags.Plans).RequireAuthorization(ApiScopes.Platform)
            .Produces<PlanAssignedResponse>()
            .WithRequestExamples(ExampleBodies.AssignPlan)
            .WithOperationId("assignCompanyPlan")
            .WithSummary("Asignar un plan a una empresa")
            .WithDescription(
                "Asigna a la empresa del `{nit}` el plan `plan_id`. El plan empieza ahora y queda activo; los que la empresa " +
                "tenía activos se desactivan. Volver a asignar el mismo plan lo renueva: el periodo empieza de nuevo y el " +
                "consumo vuelve a cero. Plan o empresa que no existen: 404 (`plan.not_found`, `company.not_found`).");
    }

    private static async Task<IResult> StorePlan(StorePlanCommand body, IHandler<StorePlanCommand, PlanSavedResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body, ct)).ToHttpResult();

    /// <summary>Intentos del alta cuando otra alta simultánea tomó el mismo id (choque de llave primaria al guardar).</summary>
    private const int CreatePlanAttempts = 5;

    /// <summary>
    /// El caso de uso calcula el id siguiente dentro de su transacción; si otra alta guardó ese id primero, el guardado
    /// choca con la llave primaria y la transacción se revierte con <c>persistence.conflict</c>. Se repite entonces la
    /// solicitud completa, que ya ve el plan de la otra alta y toma el id siguiente.
    /// </summary>
    private static async Task<IResult> CreatePlan(
        CreatePlanCommand body, IHandler<CreatePlanCommand, PlanSavedResponse> handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(body, ct);
        for (var attempt = 1; attempt < CreatePlanAttempts && !result.IsSuccess && result.Error.Code == "persistence.conflict"; attempt++)
        {
            result = await handler.HandleAsync(body, ct);
        }

        return result.ToHttpResult(saved => TypedResults.Created($"/api/v1/plans/{saved.Plan.Id}", saved));
    }

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
