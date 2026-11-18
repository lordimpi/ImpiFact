using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Features.Catalogs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ImpiFact.Api.Catalogs;

/// <summary>
/// Catálogos DIAN (Dirección de Impuestos y Aduanas Nacionales) para las listas de los formularios del panel
/// (Arquitectura frontend, sección 16, punto 9). Son los mismos para todas las empresas y no cambian entre despliegues,
/// así que piden cualquier credencial válida, sin alcance ni empresa activa, y la respuesta se puede guardar en caché:
/// <c>Cache-Control: public</c> por una hora y un <c>ETag</c> con la versión del catálogo, que responde 304 si no cambió.
/// </summary>
internal sealed class CatalogEndpoints : IEndpointModule
{
    /// <summary>Tiempo que el navegador o un proxy pueden usar la respuesta guardada sin volver a preguntar.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var names = string.Join(", ", CatalogDefinitions.All.Select(definition => $"`{definition.Name}`"));
        var paged = string.Join(" y ", CatalogDefinitions.All.Where(definition => definition.Paged).Select(definition => $"`{definition.Name}`"));
        endpoints.MapGroup("/api/v1/catalogs").WithTags(ApiTags.Catalogs).RequireAuthorization()
            .MapGet("/{name}", ListCatalog)
            .Produces<CatalogListResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .WithOperationId("listCatalog")
            .WithSummary("Listar un catálogo de la DIAN")
            .WithDescription(
                $"Filas del catálogo `{{name}}`: {names}. Cada fila trae `id` (el que esperan los campos `*_id` de la emisión, " +
                "de la empresa y de los maestros), `code` (código DIAN), `name` e `is_active`; los municipios suman " +
                "`department_id`, los departamentos `country_id` y los impuestos `description`. Solo salen las filas activas, " +
                "salvo con `include_inactive=true`. `q` busca el texto dentro del código o del nombre sin distinguir " +
                "mayúsculas ni tildes (`bogota` encuentra «Bogotá»). El orden es por `id`. Los catálogos largos (" + paged +
                ") se paginan por cursor: `limit` (50 si no llega, máximo 200) y `cursor` (el `next_cursor` de la página " +
                "anterior; nulo en la última); los demás salen completos y `next_cursor` es nulo. `version` cambia cuando " +
                "cambia cualquier fila del catálogo y es el `ETag` de la respuesta: con `If-None-Match` igual responde 304 " +
                "sin cuerpo. `Cache-Control: public, max-age=3600`. Pide cualquier credencial, sin alcance ni empresa " +
                "activa. Un catálogo que no se publica responde 404 (`catalog.not_found`); un parámetro no válido, 422.");
    }

    private static async Task<IResult> ListCatalog(
        string name,
        [FromQuery(Name = "q")] string? q,
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "include_inactive")] string? includeInactive,
        HttpContext context,
        IHandler<ListCatalogQuery, CatalogListResponse> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new ListCatalogQuery(name, q, cursor, limit, includeInactive), ct);
        if (!result.IsSuccess)
        {
            return result.ToHttpResult();
        }

        // La respuesta de una URL depende solo de la versión del catálogo (que incluye la forma de la respuesta) y de
        // sus parámetros, que son parte de la URL: la versión sirve de ETag fuerte.
        var etag = new EntityTagHeaderValue($"\"{result.Value.Version}\"");
        var headers = context.Response.GetTypedHeaders();
        headers.ETag = etag;
        headers.CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = MaxAge };
        var ifNoneMatch = context.Request.GetTypedHeaders().IfNoneMatch;
        return ifNoneMatch.Any(candidate => candidate.Equals(EntityTagHeaderValue.Any) || candidate.Compare(etag, useStrongComparison: false))
            ? TypedResults.StatusCode(StatusCodes.Status304NotModified)
            : TypedResults.Ok(result.Value);
    }
}
