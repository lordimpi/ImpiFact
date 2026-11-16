using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
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
    private const string Tag = "Documents";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        foreach (var prefix in new[] { "/api", "/api/v1" })
        {
            var group = endpoints.MapGroup(prefix).WithTags(Tag).RequireAuthorization(ApiScopes.DocumentsRead);
            group.MapGet("/information/{nit}", Information);
            group.MapGet("/information/{nit}/{desde}", Information);
            group.MapGet("/information/{nit}/{desde}/{hasta}", Information);
            group.MapGet("/information/{nit}/page/{page}/page", InformationPage);
            group.MapGet("/total_documents/{nit}", TotalDocuments);
            group.MapGet("/total_documents/{nit}/{desde}", TotalDocuments);
            group.MapGet("/total_documents/{nit}/{desde}/{hasta}", TotalDocuments);
        }

        var documents = endpoints.MapGroup("/api/v1/documents").WithTags(Tag).RequireAuthorization(ApiScopes.DocumentsRead);
        documents.MapGet("/", List);
        documents.MapGet("/{id:guid}", Detail);
        documents.MapGet("/by-cufe/{cufe}", DetailByCufe);
    }

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
