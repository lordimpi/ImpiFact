using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Dashboard;

namespace ImpiFact.Api.Dashboard;

/// <summary>
/// Tablero del panel (Arquitectura frontend, sección 16, punto 6). Sin equivalente en el PHP. Pide
/// <c>documents:read</c>, el alcance de todos los roles de empresa y de las API keys de consulta; todo es de la empresa
/// activa de la credencial.
/// </summary>
internal sealed class DashboardEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var dashboard = endpoints.MapGroup("/api/v1/dashboard").WithTags(ApiTags.Dashboard).RequireAuthorization(ApiScopes.DocumentsRead);
        dashboard.MapGet("/summary", Summary)
            .Produces<DashboardSummaryResponse>()
            .WithOperationId("getDashboardSummary")
            .WithSummary("Resumen del tablero de la empresa")
            .WithDescription(
                "Lo que muestra el tablero, armado en el servidor con una cantidad fija de consultas: `current_month`, los " +
                "documentos emitidos en el mes actual por estado (`by_status`, solo los estados con documentos, de más a " +
                "menos); `months`, los últimos 12 meses del más antiguo al actual (también los vacíos) con los documentos de " +
                "cada familia (`invoice`, `credit_note`, `debit_note`, `support_document`, `support_document_adjustment_note` " +
                "y `other`, los demás tipos) en todos los estados; `plan`, el plan " +
                "vigente con lo usado, el límite y lo que queda por familia (`limit` y `remaining` nulos si no tiene límites) " +
                "y el fin del periodo (`period_end`), o nulo sin plan contratado; `certificate`, el certificado de firma " +
                "activo con los días que le quedan (`days_left`), o nulo; y `resolutions`, las resoluciones activas a las que " +
                "les queda menos del 10 % del rango o menos de 30 días de vigencia (las mismas reglas de los avisos de la " +
                "campana). Los meses y los días se cuentan en el calendario de Colombia.");
    }

    private static async Task<IResult> Summary(IHandler<GetDashboardSummaryQuery, DashboardSummaryResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetDashboardSummaryQuery(), ct)).ToHttpResult();
}
