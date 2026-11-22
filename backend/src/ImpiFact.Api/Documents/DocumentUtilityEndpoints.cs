using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Application.Features.Documents.Maintenance;
using ImpiFact.Application.Features.Documents.Queries.DianStatus;
using ImpiFact.Application.Features.Documents.Queries.Download;
using ImpiFact.Application.Features.Documents.Queries.MergePdfs;

namespace ImpiFact.Api.Documents;

/// <summary>
/// Rutas utilitarias de documentos de la primera entrega (carpetas 03 «Consultas de estado», 05 «Utilidades» y 10
/// «Regenerar PDFs» del Postman): estado en la DIAN, XML validado y descargas con el alcance <c>documents:read</c>;
/// regenerar el PDF o el AttachedDocument y reenviar el correo con <c>documents:write</c>. Las heredadas van bajo
/// <c>/api/ubl2.1</c> con los mismos verbos del PHP; las canónicas, bajo <c>/api/v1/documents</c>. Ninguna llama a la
/// DIAN (la autoridad tributaria de Colombia) ni espera al Worker: las consultas responden con lo que guardó el Worker
/// y las regeneraciones y el correo quedan en la bandeja de salida (decisión C5 de la fase 4). El segmento opcional
/// <c>{GuardarEn}</c> del PHP (una carpeta del servidor) se acepta y se ignora. Cada endpoint solo arma la solicitud,
/// llama al caso de uso y traduce el resultado.
/// </summary>
internal sealed class DocumentUtilityEndpoints : IEndpointModule
{
    /// <summary><c>type_response</c> de la descarga que responde el archivo en Base64 dentro de un JSON.</summary>
    public const string Base64Response = "BASE64";

    /// <summary><c>type_response</c> de la descarga que muestra el archivo en el navegador en vez de descargarlo.</summary>
    public const string InlineResponse = "INLINE";

    /// <summary>Regla de las tareas de mantenimiento: se encolan y solo aplican a documentos aceptados.</summary>
    private const string QueuedRules =
        " La respuesta es inmediata: la tarea queda en cola y la hace el Worker. Solo aplica a documentos aceptados por la " +
        "DIAN: si no, 409 (`document.not_accepted`); un documento que no existe o es de otra empresa responde 404.";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Contrato heredado del PHP: sigue activo para la colección Postman, fuera de la documentación; se borra en la fase 7.
        var legacy = endpoints.MapGroup("/api/ubl2.1").WithTags(ApiTags.Documents)
            .RequireAuthorization(ApiScopes.DocumentsRead).ExcludeFromDescription();
        legacy.MapPost("/status/document/{trackId}", DocumentStatus);
        legacy.MapPost("/status/document/{trackId}/{saveIn}", DocumentStatus);
        legacy.MapPost("/status/zip/{trackId}", ZipStatus);
        legacy.MapPost("/status/zip/{trackId}/{saveIn}", ZipStatus);
        legacy.MapPost("/xml/document/{trackId}", DocumentXml);
        legacy.MapPost("/xml/document/{trackId}/{saveIn}", DocumentXml);
        legacy.MapGet("/download/{nit}/{file}/{typeResponse?}", Download);

        // El PHP también publicaba la descarga fuera de /ubl2.1.
        endpoints.MapGet("/api/download/{nit}/{file}/{typeResponse?}", Download).WithTags(ApiTags.Documents)
            .RequireAuthorization(ApiScopes.DocumentsRead).ExcludeFromDescription();

        var canonical = endpoints.MapGroup("/api/v1/documents").WithTags(ApiTags.Documents).RequireAuthorization(ApiScopes.DocumentsRead);
        canonical.MapGet("/by-cufe/{trackId}/dian-status", DocumentStatus)
            .Produces<DianStatusResponse>()
            .WithOperationId("getDocumentDianStatus")
            .WithSummary("Estado de un documento en la DIAN")
            .WithDescription(
                "Responde la última respuesta de la DIAN para el documento con ese CUFE o CUDE (`ResponseDian`, nulo si " +
                "todavía no hay respuesta) y el documento de ImpiFact con su estado. No llama a la DIAN: muestra lo que " +
                "guardó el Worker al enviar o al volver a consultar. Documento que no existe o de otra empresa: 404.");
        canonical.MapGet("/by-cufe/{trackId}/xml", DocumentXml)
            .Produces<DocumentXmlResponse>()
            .WithOperationId("getDocumentXml")
            .WithSummary("XML firmado de un documento aceptado")
            .WithDescription(
                "Devuelve en Base64 el XML firmado del documento con ese CUFE o CUDE, el mismo que validó la DIAN, con la " +
                "forma de la respuesta GetXmlByDocumentKey de la DIAN. Solo para documentos aceptados: si no, 409 " +
                "(`document.not_accepted`).");
        canonical.MapGet("/zip/{trackId}/dian-status", ZipStatus)
            .Produces<DianStatusResponse>()
            .WithOperationId("getTestSetZipStatus")
            .WithSummary("Estado de un envío del set de pruebas")
            .WithDescription(
                "Estado de un envío del set de pruebas de habilitación por su ZipKey (la clave que devuelve la DIAN al " +
                "recibir el set). Muestra la última respuesta de GetStatusZip que guardó el Worker; no llama a la DIAN.");
        canonical.MapGet("/{id:guid}/files/{kind}", DownloadById)
            .Produces<byte[]>(StatusCodes.Status200OK, "application/xml", "application/pdf", "application/zip")
            .WithOperationId("downloadDocumentFile")
            .WithSummary("Descargar un archivo del documento")
            .WithDescription(
                "Descarga la versión más reciente de un archivo del documento. `{kind}` es `xml` (XML firmado), `pdf`, " +
                "`attached-document` (AttachedDocument, el contenedor que se entrega al comprador) o `zip` (el ZIP del " +
                "correo, con el AttachedDocument y el PDF). El PDF, el AttachedDocument y el ZIP existen después de la " +
                "aceptación. Archivo que todavía no existe: 404 (`document.file_not_found`).");

        canonical.MapPost("/pdf/merge", MergePdfs)
            .Produces(StatusCodes.Status200OK, contentType: MergeDocumentPdfsQuery.ContentType)
            .WithRequestExamples(ExampleBodies.MergePdfs)
            .WithOperationId("mergeDocumentPdfs")
            .WithSummary("Unir los PDF de varios documentos")
            .WithDescription(
                "Une en un solo PDF (`documentos-unidos.pdf`) la versión más reciente del PDF de cada documento de " +
                "`document_ids` (de 1 a 50 ids, los que devuelve `GET /api/v1/documents`), en el orden de la lista; un id " +
                "repetido repite sus páginas. Es lo mismo que `join-pdfs` del PHP, que unía los archivos por prefijo y " +
                "número. Un documento que no existe o es de otra empresa responde 404 (`document.not_found`) para toda la " +
                "solicitud, sin unir nada. Si algún documento todavía no tiene PDF (se genera después de la aceptación de " +
                "la DIAN), responde 422 (`documents.pdf_missing`) con sus números. Si el conversor de PDF no responde: 503 " +
                "(`pdf.unavailable`, reintente después de `Retry-After`).");

        var legacyWrite = endpoints.MapGroup("/api/ubl2.1").WithTags(ApiTags.Documents)
            .RequireAuthorization(ApiScopes.DocumentsWrite).ExcludeFromDescription();
        legacyWrite.MapPost("/regeneratepdf", (RegeneratePdfCommand body, IHandler<RegeneratePdfCommand, MaintenanceQueuedResponse> handler,
            CancellationToken ct) => Queue(body with { DocumentId = null }, handler, ct));
        legacyWrite.MapPost("/regeneratepdf/{prefix}/{number}/{cufe}", (string prefix, string number, string cufe,
            IHandler<RegeneratePdfCommand, MaintenanceQueuedResponse> handler, CancellationToken ct) =>
            Queue(new RegeneratePdfCommand { Prefix = prefix, Number = new LegacyScalar(number, LegacyScalarKind.String), Cufe = cufe }, handler, ct));
        legacyWrite.MapPost("/regenerate-attached-document", (RegenerateAttachedDocumentCommand body,
            IHandler<RegenerateAttachedDocumentCommand, MaintenanceQueuedResponse> handler, CancellationToken ct) =>
            Queue(body with { DocumentId = null }, handler, ct));
        legacyWrite.MapPost("/send-email", (SendDocumentEmailCommand body, IHandler<SendDocumentEmailCommand, MaintenanceQueuedResponse> handler,
            CancellationToken ct) => Queue(body with { DocumentId = null }, handler, ct));

        var canonicalWrite = endpoints.MapGroup("/api/v1/documents").WithTags(ApiTags.Documents).RequireAuthorization(ApiScopes.DocumentsWrite);
        canonicalWrite.MapPost("/{id:guid}/pdf/regenerate", (Guid id, IHandler<RegeneratePdfCommand, MaintenanceQueuedResponse> handler,
            CancellationToken ct) => Queue(new RegeneratePdfCommand { DocumentId = id }, handler, ct))
            .Produces<MaintenanceQueuedResponse>()
            .WithOperationId("regenerateDocumentPdf")
            .WithSummary("Regenerar el PDF")
            .WithDescription(
                "Vuelve a generar el PDF del documento con sus datos guardados; queda como una revisión nueva y las " +
                "descargas entregan la más reciente." + QueuedRules);
        canonicalWrite.MapPost("/{id:guid}/attached-document/regenerate", (Guid id,
            IHandler<RegenerateAttachedDocumentCommand, MaintenanceQueuedResponse> handler, CancellationToken ct) =>
            Queue(new RegenerateAttachedDocumentCommand { DocumentId = id }, handler, ct))
            .Produces<MaintenanceQueuedResponse>()
            .WithOperationId("regenerateAttachedDocument")
            .WithSummary("Regenerar el AttachedDocument")
            .WithDescription(
                "Vuelve a armar y firmar el AttachedDocument con la respuesta firmada de la DIAN (ApplicationResponse) " +
                "guardada; queda como una revisión nueva. Si la DIAN no devolvió el ApplicationResponse: 409 " +
                "(`document.application_response_missing`); sin certificado activo para firmar: 422 (`certificate.missing`)." +
                QueuedRules);
        canonicalWrite.MapPost("/{id:guid}/email", (Guid id, IHandler<SendDocumentEmailCommand, MaintenanceQueuedResponse> handler,
            CancellationToken ct) => Queue(new SendDocumentEmailCommand { DocumentId = id }, handler, ct))
            .Produces<MaintenanceQueuedResponse>()
            .WithOperationId("resendDocumentEmail")
            .WithSummary("Reenviar el correo al comprador")
            .WithDescription(
                "Vuelve a enviar el correo del documento a los destinatarios de la solicitud con que se emitió, aunque ya se " +
                "hubiera enviado. Responde 422 si esa solicitud no pidió el correo (`email.not_requested`), si el comprador " +
                "es el consumidor final sin `email_pos_customer` (`email.final_consumer`) o si no tiene un correo válido " +
                "(`email.no_recipient`)." + QueuedRules);
    }

    private static async Task<IResult> MergePdfs(
        MergeDocumentPdfsQuery body, IHandler<MergeDocumentPdfsQuery, byte[]> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body, ct)).ToHttpResult(pdf =>
            TypedResults.File(pdf, MergeDocumentPdfsQuery.ContentType, MergeDocumentPdfsQuery.FileName));

    private static async Task<IResult> Queue<TCommand>(
        TCommand command, IHandler<TCommand, MaintenanceQueuedResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToHttpResult();

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
