using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Documents.Queries.CurrentNumber;
using ImpiFact.Application.Features.Documents.Queries;
using ImpiFact.Application.Features.Documents.Queries.ExportDocuments;
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
                .WithOperationId("listValidatedDocuments")
                .WithSummary("Documentos validados (todos)")
                .WithDescription(InformationText + Dates + OwnNit);
            group.MapGet("/information/{nit}/{desde}", Information).Produces<InformationResponse>()
                .WithOperationId("listValidatedDocumentsOfDay")
                .WithSummary("Documentos validados de un día")
                .WithDescription(InformationText + Dates + OwnNit);
            group.MapGet("/information/{nit}/{desde}/{hasta}", Information).Produces<InformationResponse>()
                .WithOperationId("listValidatedDocumentsBetween")
                .WithSummary("Documentos validados entre dos fechas")
                .WithDescription(InformationText + Dates + OwnNit);
            group.MapGet("/information/{nit}/page/{page}/page", InformationPage).Produces<InformationResponse>()
                .WithOperationId("listValidatedDocumentsPage")
                .WithSummary("Documentos validados por página")
                .WithDescription(
                    "Los mismos documentos de `information`, sin filtro de fechas: 100 por tipo y por página, del más antiguo " +
                    "al más nuevo. `{page}` empieza en 1." + OwnNit);
            group.MapGet("/total_documents/{nit}", TotalDocuments).Produces<TotalDocumentsResponse>()
                .WithOperationId("countValidatedDocuments")
                .WithSummary("Cantidad de documentos validados (todos)")
                .WithDescription(TotalsText + Dates + OwnNit);
            group.MapGet("/total_documents/{nit}/{desde}", TotalDocuments).Produces<TotalDocumentsResponse>()
                .WithOperationId("countValidatedDocumentsOfDay")
                .WithSummary("Cantidad de documentos validados de un día")
                .WithDescription(TotalsText + Dates + OwnNit);
            group.MapGet("/total_documents/{nit}/{desde}/{hasta}", TotalDocuments).Produces<TotalDocumentsResponse>()
                .WithOperationId("countValidatedDocumentsBetween")
                .WithSummary("Cantidad de documentos validados entre dos fechas")
                .WithDescription(TotalsText + Dates + OwnNit);
        }

        // Contrato del PHP: invoice/current_number/{type}/{prefix?}/{ignore_state_document_id?}.
        var legacyInvoice = endpoints.MapGroup("/api/ubl2.1/invoice").WithTags(ApiTags.Documents)
            .RequireAuthorization(ApiScopes.DocumentsRead).ExcludeFromDescription();
        legacyInvoice.MapGet("/current_number/{type:int}", CurrentNumber);
        legacyInvoice.MapGet("/current_number/{type:int}/{prefix}", CurrentNumber);
        legacyInvoice.MapGet("/current_number/{type:int}/{prefix}/{ignore}", CurrentNumber);

        // Contrato del PHP: next-consecutive con {type_document_id, prefix}; la misma regla de current_number.
        endpoints.MapPost("/api/ubl2.1/next-consecutive", NextConsecutive).WithTags(ApiTags.Documents)
            .RequireAuthorization(ApiScopes.DocumentsRead).ExcludeFromDescription();

        var documents = endpoints.MapGroup("/api/v1/documents").WithTags(ApiTags.Documents).RequireAuthorization(ApiScopes.DocumentsRead);
        documents.MapGet("/current-number/{type:int}", (int type, [FromQuery(Name = "prefix")] string? prefix,
            IHandler<GetCurrentNumberQuery, CurrentNumberResponse> handler, CancellationToken ct) => CurrentNumber(type, prefix, handler, ct))
            .Produces<CurrentNumberResponse>()
            .WithOperationId("getNextDocumentNumber")
            .WithSummary("Próximo número de una resolución")
            .WithDescription(
                "Responde el número que tomará el próximo documento del tipo `{type}` (1 factura, 4 nota crédito, 5 nota " +
                "débito…) con la resolución activa del prefijo `prefix` (sin prefijo, la primera resolución activa del " +
                "tipo). `range_exceeded` es verdadero si ese número ya está fuera del rango autorizado. Solo consulta: no " +
                "reserva el número, así que dos consultas seguidas responden lo mismo hasta que se emita un documento.");
        documents.MapGet("/", List)
            .Produces<DocumentListResponse>()
            .WithOperationId("listDocuments")
            .WithSummary("Listar los documentos de la empresa")
            .WithDescription(
                "Documentos de la empresa de la credencial, del más reciente al más antiguo (orden `issued_at` y luego `id`, " +
                "ambos descendentes)." + ListFilters + " Pagina por cursor: `limit` (50 si no llega, máximo 200; el panel pide " +
                "hasta 100) y `cursor` (el `next_cursor` de la página anterior; nulo en la última). Con `include_total=true` " +
                "la respuesta trae además `total_count`, la cantidad de documentos que cumplen los filtros sin importar la " +
                "página: cuesta una consulta más que recorre todas esas filas, así que conviene pedirlo solo en la primera " +
                "página o al cambiar los filtros. Cada fila trae `kind` (`invoice`, `credit_note`, `debit_note` o nulo para " +
                "otros tipos, también el documento soporte y su nota de ajuste) y `email_status` (`NotSent`, `Sent` o `Failed`). Un filtro no válido responde 422.");
        documents.MapGet("/export.csv", Export)
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .WithOperationId("exportDocumentsCsv")
            .WithSummary("Exportar los documentos de la empresa a CSV")
            .WithDescription(
                "Los documentos del listado en un archivo CSV para hoja de cálculo, con los mismos filtros de " +
                "`GET /api/v1/documents` y el mismo orden, sin cursor ni límite." + ListFilters + " Formato pensado para " +
                "abrir con doble clic en español de Colombia: UTF-8 con BOM, separador `;`, fin de línea CRLF, importes con " +
                "coma decimal y sin separador de miles (`1234567,89`), fechas `yyyy-MM-dd` y horas en hora de Colombia, " +
                "encabezados y estados en español. Columnas: Tipo, Número, Fecha de emisión, Hora de emisión, Identificación " +
                "del cliente, Cliente, Subtotal, Impuestos, Total, Estado, Estado del correo y CUFE/CUDE. Un texto que " +
                "empieza por `=`, `+`, `-` o `@` lleva un apóstrofo delante para que la hoja no lo ejecute como fórmula. El " +
                "archivo se escribe mientras se lee de la base. Máximo " + ExportMax + " filas: si los filtros dan más, responde " +
                "422 (`documents.export_too_large`) antes de escribir nada; acote por fechas u otros filtros. Un filtro no " +
                "válido responde 422.");
        documents.MapGet("/export.xlsx", ExportXlsx)
            .Produces(StatusCodes.Status200OK, contentType: DocumentExport.XlsxContentType)
            .WithOperationId("exportDocumentsXlsx")
            .WithSummary("Exportar los documentos de la empresa a Excel")
            .WithDescription(
                "Los mismos documentos, filtros, orden y columnas de `GET /api/v1/documents/export.csv`, en un libro de " +
                "Excel (.xlsx) `documentos.xlsx` con una hoja «Documentos»." + ListFilters + " Encabezados en español y en " +
                "negrita, con ancho de columna ajustado. Los importes (Subtotal, Impuestos y Total) son celdas numéricas con " +
                "separador de miles y dos decimales; la fecha de emisión es una fecha (dd/mm/aaaa) y la hora de emisión una " +
                "hora (hh:mm:ss), ambas en hora de Colombia; el estado y el estado del correo van en español. Los textos van " +
                "como texto, nunca como fórmula. El archivo se escribe mientras se lee de la base. Máximo " + ExportMax +
                " filas: si los filtros dan más, responde 422 (`documents.export_too_large`) antes de escribir nada; acote " +
                "por fechas u otros filtros. Un filtro no válido responde 422.");
        documents.MapGet("/export.pdf", ExportPdf)
            .Produces(StatusCodes.Status200OK, contentType: ExportDocumentsPdfQuery.ContentType)
            .WithOperationId("exportDocumentsPdf")
            .WithSummary("Exportar los documentos de la empresa a PDF")
            .WithDescription(
                "Los mismos documentos, filtros y orden de `GET /api/v1/documents/export.csv`, en un reporte PDF " +
                "`documentos.pdf` para imprimir o compartir." + ListFilters + " Hoja A4 horizontal. El encabezado lleva la " +
                "razón social y el NIT de la empresa, el título «Documentos», los filtros aplicados en español (fechas " +
                "dd/mm/aaaa; «Todos» si un filtro no llega) y la fecha y hora de generación en hora de Colombia. La tabla " +
                "lleva las columnas del CSV salvo CUFE/CUDE, que no cabe en la hoja (el CSV y el Excel sí lo traen): Tipo, " +
                "Número, Fecha de emisión, Hora de emisión, Identificación del cliente, Cliente, Subtotal, Impuestos, Total, " +
                "Estado y Estado del correo. Los importes van con separador de miles y coma decimal (`1.234.567,89`), " +
                "alineados a la derecha; la fila de encabezados se repite en cada página, el pie dice «Página X de Y» y la " +
                "última línea da la cantidad de documentos. Máximo " + ExportPdfMax + " filas, menos que el CSV y el Excel " +
                "porque el reporte se arma completo antes de convertirlo: si los filtros dan más, responde 422 " +
                "(`documents.export_too_large`) sin generar nada; descargue en Excel o acote por fechas u otros filtros. Si " +
                "el conversor de PDF no responde: 503 (`pdf.unavailable`, reintente después de `Retry-After`). Un filtro no " +
                "válido responde 422.");
        documents.MapGet("/{id:guid}", Detail)
            .Produces<DocumentDetailResponse>()
            .WithOperationId("getDocument")
            .WithSummary("Ver un documento por su id")
            .WithDescription(
                "Detalle del documento: número, estado (`status`), CUFE o CUDE, comprador, totales, impuestos y la última " +
                "respuesta de la DIAN (`dian_status_code`, `dian_status_description`). Es la forma recomendada de consultar " +
                "el estado después de emitir. Un documento de otra empresa responde 404, igual que uno que no existe.");
        documents.MapGet("/by-cufe/{cufe}", DetailByCufe)
            .Produces<DocumentDetailResponse>()
            .WithOperationId("getDocumentByCufe")
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

    private const string ListFilters =
        " Filtros opcionales, que se combinan entre sí: `kind` (`invoice`, `credit_note`, `debit_note`, `support_document` o " +
        "`support_document_adjustment_note`) o `document_type_id` (1 factura, 4 nota crédito, 5 nota débito, 11 documento " +
        "soporte, 13 nota de ajuste…; si llegan los dos deben coincidir); `status`, uno " +
        "o varios estados por su nombre sin distinguir mayúsculas, separados por coma o repitiendo el parámetro " +
        "(`status=Accepted,Rejected`); `from` y `to`, días de emisión `yyyy-MM-dd` en hora de Colombia, ambos incluidos; " +
        "`customer`, texto (hasta 100 caracteres) que aparece en el número de identificación o en el nombre del " +
        "adquiriente, sin distinguir mayúsculas; `number`, el número completo con prefijo (`SETP990000001`) o solo los " +
        "dígitos, que coinciden con ese número en cualquier prefijo. Un `customer` o `number` vacío no filtra.";

    private static readonly string ExportMax = ExportDocumentsQuery.MaxRows.ToString("N0", DocumentCsv.Numbers);

    private static readonly string ExportPdfMax = ExportDocumentsPdfQuery.MaxRows.ToString("N0", DocumentCsv.Numbers);

    private static async Task<IResult> CurrentNumber(
        int type, string? prefix, IHandler<GetCurrentNumberQuery, CurrentNumberResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetCurrentNumberQuery(type, prefix), ct)).ToHttpResult();

    private static async Task<IResult> NextConsecutive(
        GetNextConsecutiveQuery body, IHandler<GetNextConsecutiveQuery, NextConsecutiveResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body, ct)).ToHttpResult();

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
        [FromQuery(Name = "kind")] string? kind,
        [FromQuery(Name = "status")] string[]? status,
        [FromQuery(Name = "from")] string? from,
        [FromQuery(Name = "to")] string? to,
        [FromQuery(Name = "customer")] string? customer,
        [FromQuery(Name = "number")] string? number,
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "include_total")] bool? includeTotal,
        IHandler<ListDocumentsQuery, DocumentListResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(
            new ListDocumentsQuery(documentTypeId, Statuses(status), from, to, cursor, limit, kind, customer, number, includeTotal ?? false), ct))
        .ToHttpResult();

    /// <summary>
    /// El CSV se escribe directo en el cuerpo de la respuesta mientras se leen las filas. Un error de validación o el
    /// tope de filas responden antes de empezar a escribir.
    /// </summary>
    private static async Task<IResult> Export(
        [FromQuery(Name = "document_type_id")] int? documentTypeId,
        [FromQuery(Name = "kind")] string? kind,
        [FromQuery(Name = "status")] string[]? status,
        [FromQuery(Name = "from")] string? from,
        [FromQuery(Name = "to")] string? to,
        [FromQuery(Name = "customer")] string? customer,
        [FromQuery(Name = "number")] string? number,
        IHandler<ExportDocumentsQuery, DocumentExport> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new ExportDocumentsQuery(documentTypeId, kind, Statuses(status), from, to, customer, number), ct))
        .ToHttpResult(export => TypedResults.Stream(
            stream => export.WriteCsvAsync(stream, ct), "text/csv; charset=utf-8", export.FileName));

    /// <summary>
    /// The Excel twin of <see cref="Export"/>: the same query and errors, and the workbook written as the rows are read.
    /// </summary>
    private static async Task<IResult> ExportXlsx(
        [FromQuery(Name = "document_type_id")] int? documentTypeId,
        [FromQuery(Name = "kind")] string? kind,
        [FromQuery(Name = "status")] string[]? status,
        [FromQuery(Name = "from")] string? from,
        [FromQuery(Name = "to")] string? to,
        [FromQuery(Name = "customer")] string? customer,
        [FromQuery(Name = "number")] string? number,
        IHandler<ExportDocumentsQuery, DocumentExport> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new ExportDocumentsQuery(documentTypeId, kind, Statuses(status), from, to, customer, number), ct))
        .ToHttpResult(export => TypedResults.Stream(
            stream => export.WriteXlsxAsync(stream, ct), DocumentExport.XlsxContentType, export.XlsxFileName));

    /// <summary>
    /// The PDF twin of <see cref="Export"/>: the same filters and errors, a lower row cap, and the whole report converted
    /// before answering, so it goes out as one downloaded file.
    /// </summary>
    private static async Task<IResult> ExportPdf(
        [FromQuery(Name = "document_type_id")] int? documentTypeId,
        [FromQuery(Name = "kind")] string? kind,
        [FromQuery(Name = "status")] string[]? status,
        [FromQuery(Name = "from")] string? from,
        [FromQuery(Name = "to")] string? to,
        [FromQuery(Name = "customer")] string? customer,
        [FromQuery(Name = "number")] string? number,
        IHandler<ExportDocumentsPdfQuery, byte[]> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new ExportDocumentsPdfQuery(documentTypeId, kind, Statuses(status), from, to, customer, number), ct))
        .ToHttpResult(pdf => TypedResults.File(pdf, ExportDocumentsPdfQuery.ContentType, ExportDocumentsPdfQuery.FileName));

    /// <summary>Junta <c>status=A&amp;status=B</c> y <c>status=A,B</c> en una sola lista separada por coma.</summary>
    private static string? Statuses(string[]? status) => status is null || status.Length == 0 ? null : string.Join(',', status);

    private static async Task<IResult> Detail(Guid id, IHandler<GetDocumentQuery, DocumentDetailResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetDocumentQuery(id), ct)).ToHttpResult();

    private static async Task<IResult> DetailByCufe(string cufe, IHandler<GetDocumentByCufeQuery, DocumentDetailResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetDocumentByCufeQuery(cufe), ct)).ToHttpResult();
}
