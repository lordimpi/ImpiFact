using System.Text.Json.Serialization;
using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Application.Features.Documents.Queries.Download;
using ImpiFact.Application.Features.Documents.Radian;
using Microsoft.AspNetCore.Mvc;

namespace ImpiFact.Api.Radian;

/// <summary>
/// RADIAN routes (phase 7a, T6; Arquitectura detallada, RADIAN section). Names: <c>/api/v1/received-documents</c> is the
/// collection of the invoices the company received (a resource of its own, because those invoices are not documents the
/// company issued), and <c>/events</c> under a received or an own document is the collection of the events of that
/// invoice; <c>/dian-events</c> is what the DIAN lists for it (GetStatusEvent). Reading asks for <c>documents:read</c>;
/// loading a document and sending an event, for <c>documents:write</c>. Every route acts only on the company of the
/// credential: a document of another company answers 404 like a missing one (php-rules 7, rows 13, 16 and 17). The PHP
/// routes were deleted in phase 7a, T9b2.
/// </summary>
internal sealed class RadianEndpoints : IEndpointModule
{
    private const string Isolation = " Un documento que no existe o es de otra empresa responde 404.";

    private const string SendRules =
        " ImpiFact revisa antes el orden del anexo técnico 1.9 (reglas LGC01 a LGC14): un evento de cada tipo, 030 antes del " +
        "032, el 031 y el 033 solo después del 032, el reclamo y la aceptación se excluyen, y el 034 lo registra el emisor " +
        "pasados 3 días hábiles del 032. Si no se cumple responde 422 con el motivo, sin llamar a la DIAN. La respuesta trae " +
        "`success` verdadero solo si la DIAN registró el evento; con un rechazo, `success` falso, `event.status` `rejected` y " +
        "las reglas en `event.error_messages` (el evento rechazado se puede volver a enviar). Si la DIAN no responde, " +
        "`event.status` es `unconfirmed` y un nuevo envío reenvía el mismo XML firmado. Cuenta en `qty_docs_radian` del " +
        "plan cuando la DIAN lo registra.";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var read = endpoints.MapGroup("/api/v1/received-documents").WithTags(ApiTags.Radian).RequireAuthorization(ApiScopes.DocumentsRead);
        read.MapGet("/", List)
            .Produces<ReceivedDocumentListResponse>()
            .WithOperationId("listReceivedDocuments")
            .WithSummary("Listar las facturas recibidas")
            .WithDescription(
                "Facturas que la empresa recibió de sus proveedores, de la fecha de emisión más reciente a la más antigua, " +
                "cada una con sus eventos. Filtros: `supplier_nit` (NIT exacto del proveedor), `from` y `to` (fechas de " +
                "emisión `yyyy-MM-dd`, inclusive). Pagina con `limit` (50 si no llega, máximo 200) y `offset`; " +
                "`total_count` es la cantidad que cumple los filtros.");
        read.MapGet("/{id:guid}", Get)
            .Produces<ReceivedDocumentResponse>()
            .WithOperationId("getReceivedDocument")
            .WithSummary("Detalle de una factura recibida")
            .WithDescription("La factura recibida con sus eventos, del más antiguo al más reciente." + Isolation);
        read.MapGet("/{id:guid}/xml", Xml)
            .Produces<byte[]>(StatusCodes.Status200OK, "application/xml")
            .WithOperationId("downloadReceivedDocumentXml")
            .WithSummary("XML de una factura recibida")
            .WithDescription("Descarga el XML de la factura tal como lo entregó la DIAN o el proveedor, con el nombre `{cufe}.xml`." + Isolation);
        read.MapGet("/{id:guid}/dian-events", (Guid id, IHandler<GetDianEventsQuery, DianEventListResponse> handler, CancellationToken ct) =>
                DianEvents(new GetDianEventsQuery(null, id), handler, ct))
            .Produces<DianEventListResponse>()
            .WithOperationId("getReceivedDocumentDianEvents")
            .WithSummary("Eventos registrados en la DIAN de una factura recibida")
            .WithDescription(
                "Consulta a la DIAN (GetStatusEvent) los eventos que tiene registrados la factura, también los de otros " +
                "participantes. Si la DIAN no responde: 503." + Isolation);

        var write = endpoints.MapGroup("/api/v1/received-documents").WithTags(ApiTags.Radian).RequireAuthorization(ApiScopes.DocumentsWrite);
        write.MapPost("/", Load)
            .Produces<ReceivedDocumentResponse>(StatusCodes.Status201Created)
            .Produces<ReceivedDocumentResponse>()
            .WithRequestExamples(ExampleBodies.ReceivedDocument)
            .WithOperationId("loadReceivedDocument")
            .WithSummary("Cargar una factura recibida por su CUFE")
            .WithDescription(
                "Descarga de la DIAN (GetXmlByDocumentKey) la factura con ese `cufe` y la guarda con su XML: 201 con la " +
                "factura. Si la empresa ya la tiene, 200 con la guardada, sin llamar a la DIAN. La factura debe estar dirigida " +
                "a la empresa de la credencial (su adquiriente): si no, 422 (`radian.wrong_buyer`). Un CUFE que no es de una " +
                "factura responde 422 (`radian.not_an_invoice`) y uno que la DIAN no encuentra, 422 " +
                "(`radian.dian_document_not_found`); si la DIAN no está disponible, 503.");
        write.MapPost("/{id:guid}/events", (Guid id, SendEventBody body, IHandler<SendReceivedDocumentEventCommand, RadianEventResponse> handler,
                CancellationToken ct) => Send(new SendReceivedDocumentEventCommand(id, body), handler, ct))
            .Produces<RadianEventResponse>()
            .WithRequestExamples(ExampleBodies.ReceivedDocumentEvents)
            .WithOperationId("sendReceivedDocumentEvent")
            .WithSummary("Registrar un evento sobre una factura recibida")
            .WithDescription(
                "Arma, firma y envía a la DIAN (SendEventUpdateStatus) el evento `event_code` de la empresa como adquiriente: " +
                "`030` acuse de recibo, `031` reclamo (con `claim_reason` `01` a `04`), `032` recibo del bien o prestación " +
                "del servicio, `033` aceptación expresa. `issuer_party` es la persona que recibe o decide; sin él va la " +
                "empresa." + SendRules + Isolation);

        var documentsRead = endpoints.MapGroup("/api/v1/documents").WithTags(ApiTags.Radian).RequireAuthorization(ApiScopes.DocumentsRead);
        documentsRead.MapGet("/{id:guid}/events", Events)
            .Produces<DocumentEventListResponse>()
            .WithOperationId("listDocumentEvents")
            .WithSummary("Eventos enviados sobre una factura propia")
            .WithDescription("Los eventos que la empresa envió sobre su factura (la aceptación tácita, 034), con su estado en la DIAN." + Isolation);
        documentsRead.MapGet("/{id:guid}/dian-events", (Guid id, IHandler<GetDianEventsQuery, DianEventListResponse> handler, CancellationToken ct) =>
                DianEvents(new GetDianEventsQuery(id, null), handler, ct))
            .Produces<DianEventListResponse>()
            .WithOperationId("getDocumentDianEvents")
            .WithSummary("Eventos registrados en la DIAN de una factura propia")
            .WithDescription(
                "Consulta a la DIAN (GetStatusEvent) los eventos de la factura, entre ellos los que registró el adquiriente " +
                "(030 a 033). Si la DIAN no responde: 503." + Isolation);

        var documentsWrite = endpoints.MapGroup("/api/v1/documents").WithTags(ApiTags.Radian).RequireAuthorization(ApiScopes.DocumentsWrite);
        documentsWrite.MapPost("/{id:guid}/events", (Guid id, SendEventBody body, IHandler<SendDocumentEventCommand, RadianEventResponse> handler,
                CancellationToken ct) => Send(new SendDocumentEventCommand(id, body), handler, ct))
            .Produces<RadianEventResponse>()
            .WithRequestExamples(ExampleBodies.TacitAcceptance)
            .WithOperationId("sendDocumentEvent")
            .WithSummary("Registrar la aceptación tácita de una factura propia")
            .WithDescription(
                "Arma, firma y envía la aceptación tácita (`event_code` `034`) de una factura de venta propia aceptada por la " +
                "DIAN, con la nota de declaración bajo juramento y la DIAN como receptor. Antes consulta a la DIAN " +
                "(GetStatusEvent) los eventos del adquiriente: el 034 requiere el 032 y que no haya reclamo ni aceptación " +
                "expresa." + SendRules + Isolation);
    }

    private static async Task<IResult> List(
        [FromQuery(Name = "supplier_nit")] string? supplierNit,
        [FromQuery(Name = "from")] DateOnly? from,
        [FromQuery(Name = "to")] DateOnly? to,
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "offset")] int? offset,
        IHandler<ListReceivedDocumentsQuery, ReceivedDocumentListResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new ListReceivedDocumentsQuery(supplierNit, from, to, limit, offset), ct)).ToHttpResult();

    private static async Task<IResult> Get(Guid id, IHandler<GetReceivedDocumentQuery, ReceivedDocumentResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetReceivedDocumentQuery(id), ct)).ToHttpResult();

    private static async Task<IResult> Xml(Guid id, IHandler<GetReceivedDocumentXmlQuery, DocumentFileContent> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetReceivedDocumentXmlQuery(id), ct))
        .ToHttpResult(file => TypedResults.File(file.Content, file.ContentType, file.FileName));

    private static async Task<IResult> Load(
        LoadReceivedDocumentCommand body, IHandler<LoadReceivedDocumentCommand, LoadReceivedDocumentResult> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body, ct)).ToHttpResult(result => result.AlreadyExisted
            ? TypedResults.Ok(result.Document)
            : TypedResults.Created($"/api/v1/received-documents/{result.Document.Id}", result.Document));

    private static async Task<IResult> Send<TCommand>(TCommand command, IHandler<TCommand, RadianEventResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToHttpResult();

    private static async Task<IResult> Events(Guid id, IHandler<ListDocumentEventsQuery, DocumentEventListResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ListDocumentEventsQuery(id), ct)).ToHttpResult();

    private static async Task<IResult> DianEvents(
        GetDianEventsQuery query, IHandler<GetDianEventsQuery, DianEventListResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(query, ct)).ToHttpResult();
}
