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
/// routes keep their paths outside the documentation until phase 7a T9.
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
                DianEvents(new GetDianEventsQuery(null, id, null), handler, ct))
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
                DianEvents(new GetDianEventsQuery(id, null, null), handler, ct))
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

        MapLegacy(endpoints);
    }

    /// <summary>PHP routes (php-rules 16.1), with credential and company, outside the documentation; deleted in T9.</summary>
    private static void MapLegacy(IEndpointRouteBuilder endpoints)
    {
        var legacyWrite = endpoints.MapGroup("/api/ubl2.1").WithTags(ApiTags.Radian)
            .RequireAuthorization(ApiScopes.DocumentsWrite).ExcludeFromDescription();
        legacyWrite.MapPost("/send-event", (LegacyEventBody body, IHandler<SendEventFromAttachedDocumentCommand, RadianEventResponse> handler,
            CancellationToken ct) => Send(new SendEventFromAttachedDocumentCommand(body), handler, ct));
        legacyWrite.MapPost("/send-event-data", (LegacyEventBody body, IHandler<SendEventByCufeCommand, RadianEventResponse> handler,
            CancellationToken ct) => Send(new SendEventByCufeCommand(body), handler, ct));
        endpoints.MapPost("/api/load-document-by-cufe", LegacyLoad).WithTags(ApiTags.Radian)
            .RequireAuthorization(ApiScopes.DocumentsWrite).ExcludeFromDescription();

        var legacyRead = endpoints.MapGroup("/api/ubl2.1").WithTags(ApiTags.Radian)
            .RequireAuthorization(ApiScopes.DocumentsRead).ExcludeFromDescription();
        legacyRead.MapPost("/status/events-document/{trackId}", (string trackId, IHandler<GetDianEventsQuery, DianEventListResponse> handler,
            CancellationToken ct) => DianEvents(new GetDianEventsQuery(null, null, trackId), handler, ct));
        // The PHP read the public web page of the DIAN; ImpiFact asks GetStatusEvent (php-rules 16.8, item 2). {ambiente} is ignored.
        legacyRead.MapPost("/query-events-cufe-dian/{cufe}/{ambiente}", (string cufe, IHandler<GetDianEventsQuery, DianEventListResponse> handler,
            CancellationToken ct) => DianEvents(new GetDianEventsQuery(null, null, cufe), handler, ct));
        endpoints.MapGet("/api/receivedfile/{nit}/{file}", ReceivedFile).WithTags(ApiTags.Radian)
            .RequireAuthorization(ApiScopes.DocumentsRead).ExcludeFromDescription();
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

    private static async Task<IResult> ReceivedFile(
        string nit, string file, IHandler<DownloadReceivedFileQuery, DocumentFileContent> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new DownloadReceivedFileQuery(nit, file), ct))
        .ToHttpResult(content => TypedResults.File(content.Content, content.ContentType, content.FileName));

    private static async Task<IResult> Load(
        LoadReceivedDocumentCommand body, IHandler<LoadReceivedDocumentCommand, LoadReceivedDocumentResult> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body with { Save = true }, ct)).ToHttpResult(result => result.AlreadyExisted
            ? TypedResults.Ok(result.Document)
            : TypedResults.Created($"/api/v1/received-documents/{result.Document.Id}", result.Document));

    /// <summary>
    /// php-rules 16.7, item 4: the PHP reads <c>save</c>, but the Postman collection sends <c>saved</c>, so the PHP never
    /// stored the document from it. ImpiFact reads either; without both it only answers the data, like the PHP.
    /// </summary>
    private static async Task<IResult> LegacyLoad(
        LegacyLoadBody body, IHandler<LoadReceivedDocumentCommand, LoadReceivedDocumentResult> handler, CancellationToken ct)
    {
        var save = (body.Save ?? body.Saved) is { } flag && flag.TryBoolean(out var value) && value;
        var result = await handler.HandleAsync(new LoadReceivedDocumentCommand { Cufe = body.Cufe, Save = save }, ct);
        return result.ToHttpResult(loaded => TypedResults.Ok(new LoadReceivedDocumentResponse
        {
            Success = true,
            Message = loaded.AlreadyExisted ? "El documento ya existe en la base de datos."
                : loaded.Saved ? "El documento fue cargado y guardado satisfactoriamente."
                : "Datos del documento obtenidos satisfactoriamente.",
            Data = loaded.Document,
            Saved = loaded.AlreadyExisted ? null : loaded.Saved,
            AlreadyExists = loaded.AlreadyExisted ? true : null,
        }));
    }

    private static async Task<IResult> Send<TCommand>(TCommand command, IHandler<TCommand, RadianEventResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(command, ct)).ToHttpResult();

    private static async Task<IResult> Events(Guid id, IHandler<ListDocumentEventsQuery, DocumentEventListResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ListDocumentEventsQuery(id), ct)).ToHttpResult();

    private static async Task<IResult> DianEvents(
        GetDianEventsQuery query, IHandler<GetDianEventsQuery, DianEventListResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(query, ct)).ToHttpResult();

    /// <summary>Body of the PHP <c>load-document-by-cufe</c>.</summary>
    internal sealed record LegacyLoadBody
    {
        [JsonPropertyName("cufe")]
        public string? Cufe { get; init; }

        [JsonPropertyName("save")]
        public LegacyScalar? Save { get; init; }

        [JsonPropertyName("saved")]
        public LegacyScalar? Saved { get; init; }
    }
}
