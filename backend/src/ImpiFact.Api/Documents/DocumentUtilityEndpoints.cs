using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Queries.DianStatus;
using ImpiFact.Application.Features.Documents.Queries.Download;

namespace ImpiFact.Api.Documents;

/// <summary>
/// Rutas utilitarias de documentos de la primera entrega (carpetas 03 «Consultas de estado» y 05 «Utilidades» del
/// Postman): estado en la DIAN, XML validado y descargas, con el alcance <c>documents:read</c>. Las heredadas van bajo
/// <c>/api/ubl2.1</c> con los mismos verbos del PHP; las canónicas, bajo <c>/api/v1/documents</c>. Ninguna llama a la
/// DIAN (la autoridad tributaria de Colombia): responden con lo que guardó el Worker. El segmento opcional
/// <c>{GuardarEn}</c> del PHP (una carpeta del servidor) se acepta y se ignora. Cada endpoint solo arma la solicitud,
/// llama al caso de uso y traduce el resultado.
/// </summary>
internal sealed class DocumentUtilityEndpoints : IEndpointModule
{
    /// <summary><c>type_response</c> de la descarga que responde el archivo en Base64 dentro de un JSON.</summary>
    public const string Base64Response = "BASE64";

    /// <summary><c>type_response</c> de la descarga que muestra el archivo en el navegador en vez de descargarlo.</summary>
    public const string InlineResponse = "INLINE";

    private const string Tag = "Documents";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var legacy = endpoints.MapGroup("/api/ubl2.1").WithTags(Tag).RequireAuthorization(ApiScopes.DocumentsRead);
        legacy.MapPost("/status/document/{trackId}", DocumentStatus);
        legacy.MapPost("/status/document/{trackId}/{saveIn}", DocumentStatus);
        legacy.MapPost("/status/zip/{trackId}", ZipStatus);
        legacy.MapPost("/status/zip/{trackId}/{saveIn}", ZipStatus);
        legacy.MapPost("/xml/document/{trackId}", DocumentXml);
        legacy.MapPost("/xml/document/{trackId}/{saveIn}", DocumentXml);
        legacy.MapGet("/download/{nit}/{file}/{typeResponse?}", Download);

        // El PHP también publicaba la descarga fuera de /ubl2.1.
        endpoints.MapGet("/api/download/{nit}/{file}/{typeResponse?}", Download).WithTags(Tag).RequireAuthorization(ApiScopes.DocumentsRead);

        var canonical = endpoints.MapGroup("/api/v1/documents").WithTags(Tag).RequireAuthorization(ApiScopes.DocumentsRead);
        canonical.MapGet("/by-cufe/{trackId}/dian-status", DocumentStatus);
        canonical.MapGet("/by-cufe/{trackId}/xml", DocumentXml);
        canonical.MapGet("/zip/{trackId}/dian-status", ZipStatus);
        canonical.MapGet("/{id:guid}/files/{kind}", DownloadById);
    }

    private static async Task<IResult> DocumentStatus(
        string trackId, IHandler<GetDocumentDianStatusQuery, DianStatusResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetDocumentDianStatusQuery(trackId), ct)).ToHttpResult();

    private static async Task<IResult> ZipStatus(
        string trackId, IHandler<GetZipDianStatusQuery, DianStatusResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetZipDianStatusQuery(trackId), ct)).ToHttpResult();

    private static async Task<IResult> DocumentXml(
        string trackId, IHandler<GetDocumentXmlQuery, DocumentXmlResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetDocumentXmlQuery(trackId), ct)).ToHttpResult();

    private static async Task<IResult> Download(
        string nit, string file, string? typeResponse, IHandler<DownloadDocumentFileQuery, DocumentFileContent> handler, CancellationToken ct) =>
        FileResult(await handler.HandleAsync(new DownloadDocumentFileQuery(nit, file), ct), typeResponse);

    private static async Task<IResult> DownloadById(
        Guid id, string kind, IHandler<DownloadDocumentFileByIdQuery, DocumentFileContent> handler, CancellationToken ct) =>
        FileResult(await handler.HandleAsync(new DownloadDocumentFileByIdQuery(id, kind), ct), typeResponse: null);

    /// <summary>
    /// Como el PHP: con <c>BASE64</c>, un JSON con el archivo en Base64; con <c>INLINE</c>, el archivo para verlo en el
    /// navegador; si no, el archivo como descarga con su nombre.
    /// </summary>
    private static IResult FileResult(Result<DocumentFileContent> result, string? typeResponse) => result.ToHttpResult(file => typeResponse switch
    {
        Base64Response => TypedResults.Ok(new DownloadBase64Response(true, $"Archivo: {file.FileName} se encontro.", Convert.ToBase64String(file.Content))),
        InlineResponse => TypedResults.File(file.Content, file.ContentType),
        _ => TypedResults.File(file.Content, file.ContentType, file.FileName),
    });
}

/// <summary>Respuesta de la descarga con <c>type_response</c> <c>BASE64</c>, con los campos del PHP.</summary>
internal sealed record DownloadBase64Response(
    [property: System.Text.Json.Serialization.JsonPropertyName("success")] bool Success,
    [property: System.Text.Json.Serialization.JsonPropertyName("message")] string Message,
    [property: System.Text.Json.Serialization.JsonPropertyName("filebase64")] string FileBase64);
