using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Batches;
using ImpiFact.Application.Features.Documents.Issue;
using Microsoft.AspNetCore.Mvc;

namespace ImpiFact.Api.Documents;

/// <summary>
/// Batches of documents sent together with SendBillAsync (phase 7a, T8; php-rules 15). Only by API (S2, item 5).
/// <c>/api/v1/documents/batches</c> is the collection of the company's batches; <c>/{id}/documents/{type}</c> issues a document
/// into a batch with the same body, validation, plan, numbering and signature as its own issue route, and <c>/{id}/send</c>
/// closes and sends it. Reading asks for <c>documents:read</c>; the rest, for <c>documents:write</c>. A batch of another
/// company answers 404 like a missing one and never reaches the DIAN. The PHP routes <c>add-to-batch/{tipo}/{batch}</c> and
/// <c>send-batch/{batch}</c> (by name) were deleted in phase 7a, T9b2.
/// </summary>
internal sealed class DocumentBatchEndpoints : IEndpointModule
{
    /// <summary>The seven document types of the PHP batch (php-rules 15.1, item 1), by their PHP route names.</summary>
    public static readonly IReadOnlyList<string> Types =
        ["invoice", "invoice-aiu", "invoice-mandate", "invoice-export", "invoice-contingency", "credit-note", "debit-note"];

    private const string Isolation = " Un lote que no existe o es de otra empresa responde 404 (`batch.not_found`).";

    private const string BatchText =
        "Un lote reúne hasta 50 documentos (facturas y notas, que pueden ir mezcladas, como permite el anexo técnico 1.9, " +
        "sección 6.8) que se envían a la DIAN en un solo ZIP con SendBillAsync.";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var read = endpoints.MapGroup("/api/v1/documents/batches").WithTags(ApiTags.Batches).RequireAuthorization(ApiScopes.DocumentsRead);
        read.MapGet("/", List)
            .Produces<DocumentBatchListResponse>()
            .WithOperationId("listDocumentBatches")
            .WithSummary("Listar los lotes")
            .WithDescription(
                "Lotes de la empresa, del más reciente al más antiguo, sin sus documentos (están en el detalle). Pagina con " +
                "`limit` (50 si no llega, máximo 200) y `offset`; `total_count` es la cantidad de lotes.");
        read.MapGet("/{id:guid}", Get)
            .Produces<DocumentBatchResponse>()
            .WithOperationId("getDocumentBatch")
            .WithSummary("Detalle de un lote")
            .WithDescription(
                "El lote con su estado (`Open` abierto, `Queued` en cola, `Sent` enviado y en consulta con GetStatusZip, " +
                "`Completed` terminado, `Failed` sin llegar a la DIAN), su ZIP (`zip_file_name`), su ZipKey y cada documento con " +
                "su propio estado y las reglas de la DIAN (`error_messages`). El estado de cada documento también se consulta con " +
                "`GET /api/v1/documents/{id}`." + Isolation);

        var write = endpoints.MapGroup("/api/v1/documents/batches").WithTags(ApiTags.Batches).RequireAuthorization(ApiScopes.DocumentsWrite);
        write.MapPost("/", Create)
            .Produces<DocumentBatchResponse>(StatusCodes.Status201Created)
            .WithRequestExamples(ExampleBodies.DocumentBatches)
            .WithOperationId("createDocumentBatch")
            .WithSummary("Crear un lote")
            .WithDescription(
                BatchText + " Crea un lote abierto (`Open`): 201 con el lote. `name` es opcional (letras, números, `-` y `_`, " +
                "hasta 50; único en la empresa: uno repetido responde 409 `batch.name_in_use`); sin él, ImpiFact le pone uno.");
        DocumentIssueEndpoints.Issue(write.MapPost("/{id:guid}/documents/{type}", Add), OpenApiExamples.BatchDocuments)
            .WithOperationId("addDocumentToBatch")
            .WithSummary("Agregar un documento a un lote")
            .WithDescription(
                "Emite un documento dentro del lote abierto `{id}`. `{type}` es el tipo, con el nombre de la ruta de ApiDian: " +
                "`invoice` (factura, como `issueInvoice`), `invoice-aiu`, `invoice-mandate`, `invoice-export`, " +
                "`invoice-contingency` (tipo 03), `credit-note` o `debit-note`; otro responde 422. El cuerpo, las validaciones, el " +
                "plan, la numeración, el CUFE o CUDE y la firma son los de la ruta de emisión de ese tipo, y la respuesta es la " +
                "misma, con `document.batch_id`. El documento queda firmado y en cola (`Queued`) dentro del lote, sin llamar a la " +
                "DIAN, hasta `sendDocumentBatch`. Un lote ya enviado responde 409 (`batch.not_open`), uno con 50 documentos 422 " +
                "(`batch.full`) y uno cuyo ambiente ya no es el de la empresa, 409 (`batch.environment_mismatch`). Acepta " +
                "`Idempotency-Key`." + Isolation);
        write.MapPost("/{id:guid}/send", (Guid id, IHandler<SendDocumentBatchCommand, DocumentBatchResponse> handler, CancellationToken ct) =>
                SendAsync(new SendDocumentBatchCommand(id), handler, ct))
            .Produces<DocumentBatchResponse>()
            .WithOperationId("sendDocumentBatch")
            .WithSummary("Enviar un lote a la DIAN")
            .WithDescription(
                "Cierra el lote y lo deja en cola (`Queued`) para el Worker, que arma el ZIP con el XML firmado de cada documento y " +
                "lo envía una sola vez con SendBillAsync; con la ZipKey de la DIAN el lote pasa a `Sent` y el Worker consulta " +
                "GetStatusZip hasta tener el veredicto de cada documento: aceptado (con su AttachedDocument, PDF, correo y " +
                "consumo del plan, como un documento enviado solo) o rechazado (con sus reglas y un aviso). Un lote se envía una " +
                "sola vez: si ya se envió, 409 (`batch.not_open`); dos envíos al mismo tiempo dejan uno solo (el otro responde " +
                "409). Un lote sin documentos responde 422. Revisa la empresa activa (403 `company.inactive`) y su certificado " +
                "(422 `certificate.missing` o `certificate.expired`); el plan se revisó al agregar cada documento." + Isolation);
    }

    private static async Task<IResult> List(
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "offset")] int? offset,
        IHandler<ListDocumentBatchesQuery, DocumentBatchListResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new ListDocumentBatchesQuery(limit, offset), ct)).ToHttpResult();

    private static async Task<IResult> Get(Guid id, IHandler<GetDocumentBatchQuery, DocumentBatchResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetDocumentBatchQuery(id), ct)).ToHttpResult();

    private static async Task<IResult> Create(
        CreateDocumentBatchCommand? body, IHandler<CreateDocumentBatchCommand, DocumentBatchResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body ?? new CreateDocumentBatchCommand(), ct))
        .ToHttpResult(batch => TypedResults.Created($"/api/v1/documents/batches/{batch.Id}", batch));

    private static Task<IResult> Add(HttpRequest request, Guid id, string type, IServiceProvider services, CancellationToken ct) =>
        AddAsync(request, id, type, services, ct);

    /// <summary>Issues the body into the batch through the handler of its type, the same one its own issue route uses.</summary>
    private static Task<IResult> AddAsync(HttpRequest request, Guid batchId, string type, IServiceProvider services, CancellationToken ct) =>
        type switch
        {
            "invoice" => Issue<IssueInvoiceCommand>(request, services, (body, raw, key) => new(body, raw, key) { BatchId = batchId }, ct),
            "invoice-aiu" => Issue<IssueAiuInvoiceCommand>(request, services, (body, raw, key) => new(body, raw, key) { BatchId = batchId }, ct),
            "invoice-mandate" => Issue<IssueMandateInvoiceCommand>(request, services, (body, raw, key) => new(body, raw, key) { BatchId = batchId }, ct),
            "invoice-export" => Issue<IssueExportInvoiceCommand>(request, services, (body, raw, key) => new(body, raw, key) { BatchId = batchId }, ct),
            "invoice-contingency" => Issue<IssueContingencyInvoiceCommand>(
                request, services, (body, raw, key) => new(body, raw, key) { BatchId = batchId }, ct),
            "credit-note" => Issue<IssueCreditNoteCommand>(request, services, (body, raw, key) => new(body, raw, key) { BatchId = batchId }, ct),
            "debit-note" => Issue<IssueDebitNoteCommand>(request, services, (body, raw, key) => new(body, raw, key) { BatchId = batchId }, ct),
            _ => Task.FromResult(UnknownType(type).ToHttpResult()),
        };

    private static Task<IResult> Issue<TCommand>(
        HttpRequest request, IServiceProvider services, Func<IssueDocumentBody, byte[], string?, TCommand> command, CancellationToken ct)
        where TCommand : IIssueDocumentCommand
    {
        var handler = services.GetRequiredService<IHandler<TCommand, IssueDocumentResponse>>();
        return DocumentIssueEndpoints.IssueAsync(request, (body, raw, key) => handler.HandleAsync(command(body, raw, key), ct), ct);
    }

    private static Error UnknownType(string type) => Error.Validation(
        [new FieldError("type", $"El tipo {type} no se puede agregar a un lote. Use uno de: {string.Join(", ", Types)}.")]);

    private static async Task<IResult> SendAsync(
        SendDocumentBatchCommand command, IHandler<SendDocumentBatchCommand, DocumentBatchResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToHttpResult();
}
