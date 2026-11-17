using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Documents.Queries.CurrentNumber;
using ImpiFact.Application.Features.Documents.Queries;
using ImpiFact.Application.Features.Documents.Queries.GetDocument;
using ImpiFact.Application.Features.Documents.Queries.Information;
using ImpiFact.Application.Features.Documents.Queries.ListDocuments;
using Microsoft.AspNetCore.Mvc;

namespace ImpiFact.Api.Documents;

/// <summary>
/// Consultas de documentos (Arquitectura detallada, sección 9.3; anexo A.5.1). Todas piden el alcance
/// <c>documents:read</c>. Las rutas heredadas <c>/api/information/...</c> y <c>/api/total_documents/...</c> eran
/// públicas en el PHP; aquí piden credencial y su alias canónico va bajo <c>/api/v1</c> con el mismo caso de uso. El
/// NIT de la ruta debe ser el de la empresa de la credencial (si no, 403 sin revelar si existe). Cada endpoint solo
/// arma la solicitud, llama al caso de uso y traduce el resultado.
/// </summary>
internal sealed class DocumentQueryEndpoints : IEndpointModule
{
    /// <summary>Prefijo de las consultas heredadas del PHP, fuera de la documentación; se borran en la fase 7.</summary>
    private const string LegacyPrefix = "/api";

    private const string Dates =
        " `{desde}` y `{hasta}` son fechas `yyyy-MM-dd` en hora de Colombia; con una sola fecha se consulta ese día y, sin " +
        "fechas, todos los documentos.";

    private const string OwnNit =
        " El `{nit}` debe ser el de la empresa de la credencial; otro NIT responde 403 (`company.forbidden`).";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        foreach (var prefix in new[] { LegacyPrefix, "/api/v1" })
        {
            var group = endpoints.MapGroup(prefix).WithTags(ApiTags.Documents).RequireAuthorization(ApiScopes.DocumentsRead);
            if (prefix == LegacyPrefix)
            {
                group.ExcludeFromDescription();
            }

            group.MapGet("/information/{nit}", Information).Produces<InformationResponse>()
                .WithSummary("Documentos validados (todos)")
                .WithDescription(InformationText + Dates + OwnNit);
            group.MapGet("/information/{nit}/{desde}", Information).Produces<InformationResponse>()
                .WithSummary("Documentos validados de un día")
                .WithDescription(InformationText + Dates + OwnNit);
            group.MapGet("/information/{nit}/{desde}/{hasta}", Information).Produces<InformationResponse>()
                .WithSummary("Documentos validados entre dos fechas")
                .WithDescription(InformationText + Dates + OwnNit);
            group.MapGet("/information/{nit}/page/{page}/page", InformationPage).Produces<InformationResponse>()
                .WithSummary("Documentos validados por página")
                .WithDescription(
                    "Los mismos documentos de `information`, sin filtro de fechas: 100 por tipo y por página, del más antiguo " +
                    "al más nuevo. `{page}` empieza en 1." + OwnNit);
            group.MapGet("/total_documents/{nit}", TotalDocuments).Produces<TotalDocumentsResponse>()
                .WithSummary("Cantidad de documentos validados (todos)")
                .WithDescription(TotalsText + Dates + OwnNit);
            group.MapGet("/total_documents/{nit}/{desde}", TotalDocuments).Produces<TotalDocumentsResponse>()
                .WithSummary("Cantidad de documentos validados de un día")
                .WithDescription(TotalsText + Dates + OwnNit);
            group.MapGet("/total_documents/{nit}/{desde}/{hasta}", TotalDocuments).Produces<TotalDocumentsResponse>()
                .WithSummary("Cantidad de documentos validados entre dos fechas")
                .WithDescription(TotalsText + Dates + OwnNit);
        }

        // Contrato del PHP: invoice/current_number/{type}/{prefix?}/{ignore_state_document_id?}.
        var legacyInvoice = endpoints.MapGroup("/api/ubl2.1/invoice").WithTags(ApiTags.Documents)
            .RequireAuthorization(ApiScopes.DocumentsRead).ExcludeFromDescription();
        legacyInvoice.MapGet("/current_number/{type:int}", CurrentNumber);
        legacyInvoice.MapGet("/current_number/{type:int}/{prefix}", CurrentNumber);
        legacyInvoice.MapGet("/current_number/{type:int}/{prefix}/{ignore}", CurrentNumber);

        var documents = endpoints.MapGroup("/api/v1/documents").WithTags(ApiTags.Documents).RequireAuthorization(ApiScopes.DocumentsRead);
        documents.MapGet("/current-number/{type:int}", (int type, [FromQuery(Name = "prefix")] string? prefix,
            IHandler<GetCurrentNumberQuery, CurrentNumberResponse> handler, CancellationToken ct) => CurrentNumber(type, prefix, handler, ct))
            .Produces<CurrentNumberResponse>()
            .WithSummary("Próximo número de una resolución")
            .WithDescription(
                "Responde el número que tomará el próximo documento del tipo `{type}` (1 factura, 4 nota crédito, 5 nota " +
                "débito…) con la resolución activa del prefijo `prefix` (sin prefijo, la primera resolución activa del " +
                "tipo). `range_exceeded` es verdadero si ese número ya está fuera del rango autorizado.");
        documents.MapGet("/", List)
            .Produces<DocumentListResponse>()
            .WithSummary("Listar los documentos de la empresa")
            .WithDescription(
                "Documentos de la empresa de la credencial, del más reciente al más antiguo. Filtros opcionales: " +
                "`document_type_id`, `status` (nombre del estado, por ejemplo `Accepted` o `Rejected`), `from` y `to` " +
                "(días de emisión `yyyy-MM-dd`, hora de Colombia). Pagina por cursor: `limit` (50 si no llega, máximo 200) y " +
                "`cursor` (el `next_cursor` de la página anterior). Un filtro no válido responde 422.");
        documents.MapGet("/{id:guid}", Detail)
            .Produces<DocumentDetailResponse>()
            .WithSummary("Ver un documento por su id")
            .WithDescription(
                "Detalle del documento: número, estado (`status`), CUFE o CUDE, comprador, totales, impuestos y la última " +
                "respuesta de la DIAN (`dian_status_code`, `dian_status_description`). Es la forma recomendada de consultar " +
                "el estado después de emitir. Un documento de otra empresa responde 404, igual que uno que no existe.");
        documents.MapGet("/by-cufe/{cufe}", DetailByCufe)
            .Produces<DocumentDetailResponse>()
            .WithSummary("Ver un documento por su CUFE o CUDE")
            .WithDescription(
                "El mismo detalle de `GET /api/v1/documents/{id}`, buscando por el CUFE (factura) o el CUDE (nota), los " +
                "códigos únicos que devuelve la emisión. Un documento de otra empresa responde 404.");
    }

    private const string InformationText =
        "Documentos validados por la DIAN de la empresa (facturas, notas crédito, notas débito y documentos equivalentes POS), " +
        "agrupados por tipo, con los nombres de campo del contrato heredado. Devuelve todos los del rango sin paginar; para " +
        "volúmenes grandes use la variante por página o `GET /api/v1/documents`.";

    private const string TotalsText = "Cantidad de documentos validados por la DIAN de la empresa, por tipo de documento.";

    private static async Task<IResult> CurrentNumber(
        int type, string? prefix, IHandler<GetCurrentNumberQuery, CurrentNumberResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetCurrentNumberQuery(type, prefix), ct)).ToHttpResult();

    private static async Task<IResult> Information(
        string nit, string? desde, string? hasta, IHandler<GetInformationQuery, InformationResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetInformationQuery(nit, desde, hasta), ct)).ToHttpResult();

    private static async Task<IResult> InformationPage(
        string nit, string page, IHandler<GetInformationPageQuery, InformationResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetInformationPageQuery(nit, page), ct)).ToHttpResult();

    private static async Task<IResult> TotalDocuments(
        string nit, string? desde, string? hasta, IHandler<GetTotalDocumentsQuery, TotalDocumentsResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetTotalDocumentsQuery(nit, desde, hasta), ct)).ToHttpResult();

    private static async Task<IResult> List(
        [FromQuery(Name = "document_type_id")] int? documentTypeId,
        [FromQuery(Name = "status")] string? status,
        [FromQuery(Name = "from")] string? from,
        [FromQuery(Name = "to")] string? to,
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        IHandler<ListDocumentsQuery, DocumentListResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new ListDocumentsQuery(documentTypeId, status, from, to, cursor, limit), ct)).ToHttpResult();

    private static async Task<IResult> Detail(Guid id, IHandler<GetDocumentQuery, DocumentDetailResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetDocumentQuery(id), ct)).ToHttpResult();

    private static async Task<IResult> DetailByCufe(string cufe, IHandler<GetDocumentByCufeQuery, DocumentDetailResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetDocumentByCufeQuery(cufe), ct)).ToHttpResult();
}
