using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Documents.Calculate;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Application.Features.Documents.Preview;
using Microsoft.OpenApi;

namespace ImpiFact.Api.Documents;

/// <summary>
/// Emisión de factura, nota crédito y nota débito (Arquitectura detallada, sección 7; anexo A.1 a A.3) y el cálculo de
/// sus totales antes de emitir (<c>POST /api/v1/documents/calculate</c>) y la vista previa del PDF de la factura
/// (<c>POST /api/v1/documents/invoices/preview</c>). Todas piden el alcance
/// <c>documents:write</c>; la empresa es siempre la de la credencial, porque el cuerpo heredado no trae el NIT del
/// emisor. Las rutas heredadas <c>/api/ubl2.1/...</c> y sus variantes con <c>{testSetId}</c> (set de pruebas de
/// habilitación) usan el mismo caso de uso que las canónicas de <c>/api/v1/documents</c>. El encabezado
/// <c>Idempotency-Key</c> es opcional. Cada endpoint solo lee el cuerpo, arma la solicitud, llama al caso de uso y
/// traduce el resultado.
/// </summary>
internal sealed class DocumentIssueEndpoints : IEndpointModule
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>Lo que comparten las tres emisiones: respuesta inmediata, validaciones y errores principales.</summary>
    private const string IssueRules =
        " La respuesta es inmediata: el documento queda firmado, con su CUFE o CUDE, y en cola (`Queued`); el Worker lo " +
        "envía a la DIAN y el resultado se consulta después con `GET /api/v1/documents/{id}`. Antes de emitir se revisa: " +
        "empresa activa (403 `company.inactive`), resolución activa del tipo y el prefijo, certificado vigente (422 " +
        "`certificate.missing` o `certificate.expired`), software del ambiente actual (422 `software.missing`), plan de " +
        "la empresa (403 `plan.inactive` o `plan.limit_reached`) y los totales con las reglas de la DIAN (422 " +
        "`validation.failed`, con `errors` por campo). Sin `number` se asigna el siguiente de la resolución; un número ya " +
        "usado con otra solicitud responde 409 (`document.number_in_use`) y un rango agotado, 422. Acepta el encabezado " +
        "opcional `Idempotency-Key` (ver la introducción). Con `sendmail` el correo al comprador sale después de la " +
        "aceptación.";

    /// <summary>Regla de las notas: corrigen una factura ya aceptada.</summary>
    private const string NoteRules =
        " Si `billing_reference.uuid` es el CUFE de una factura de la empresa en ImpiFact, esa factura debe estar aceptada " +
        "por la DIAN (`Accepted` o `AcceptedWithWarnings`); si no, 422 (`document.reference_not_accepted`).";

    /// <summary>Lo que cambia en las rutas del set de pruebas de habilitación.</summary>
    private const string TestSetRules =
        " Envía el documento dentro del set de pruebas de habilitación `{testSetId}` que dio la DIAN al registrar el " +
        "software: el Worker usa SendTestSetAsync en vez de SendBillSync. Estos documentos no pasan por el plan ni cuentan " +
        "en su consumo.";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Contrato heredado del PHP: sigue activo para la colección Postman, fuera de la documentación; se borra en la fase 7.
        var legacy = endpoints.MapGroup("/api/ubl2.1").WithTags(ApiTags.Documents)
            .RequireAuthorization(ApiScopes.DocumentsWrite).ExcludeFromDescription();
        // El testSetId que da la DIAN es un GUID: así otra ruta del PHP con la misma forma (por ejemplo la vista previa
        // invoice/preeliminar-view) no se toma por un envío al set de pruebas.
        legacy.MapPost("/invoice", Invoice);
        legacy.MapPost("/invoice/{testSetId:guid}", Invoice);
        legacy.MapPost("/credit-note", CreditNote);
        legacy.MapPost("/credit-note/{testSetId:guid}", CreditNote);
        legacy.MapPost("/debit-note", DebitNote);
        legacy.MapPost("/debit-note/{testSetId:guid}", DebitNote);

        var canonical = endpoints.MapGroup("/api/v1/documents").WithTags(ApiTags.Documents).RequireAuthorization(ApiScopes.DocumentsWrite);
        Issue(canonical.MapPost("/invoices", Invoice), OpenApiExamples.Invoices)
            .WithOperationId("issueInvoice")
            .WithSummary("Emitir una factura")
            .WithDescription(InvoiceText + IssueRules);
        Issue(canonical.MapPost("/invoices/test-set/{testSetId}", Invoice), OpenApiExamples.Invoices)
            .WithOperationId("issueTestSetInvoice")
            .WithSummary("Emitir una factura del set de pruebas")
            .WithDescription(InvoiceText + TestSetRules + IssueRules);
        Issue(canonical.MapPost("/credit-notes", CreditNote), OpenApiExamples.CreditNotes)
            .WithOperationId("issueCreditNote")
            .WithSummary("Emitir una nota crédito")
            .WithDescription(CreditNoteText + NoteRules + IssueRules);
        Issue(canonical.MapPost("/credit-notes/test-set/{testSetId}", CreditNote), OpenApiExamples.CreditNotes)
            .WithOperationId("issueTestSetCreditNote")
            .WithSummary("Emitir una nota crédito del set de pruebas")
            .WithDescription(CreditNoteText + TestSetRules + IssueRules);
        Issue(canonical.MapPost("/debit-notes", DebitNote), OpenApiExamples.DebitNotes)
            .WithOperationId("issueDebitNote")
            .WithSummary("Emitir una nota débito")
            .WithDescription(DebitNoteText + NoteRules + IssueRules);
        Issue(canonical.MapPost("/debit-notes/test-set/{testSetId}", DebitNote), OpenApiExamples.DebitNotes)
            .WithOperationId("issueTestSetDebitNote")
            .WithSummary("Emitir una nota débito del set de pruebas")
            .WithDescription(DebitNoteText + TestSetRules + IssueRules);

        Issue(canonical.MapPost("/invoices/preview", PreviewInvoice), OpenApiExamples.Invoices, producesPdf: true)
            .WithOperationId("previewInvoice")
            .WithSummary("Ver la vista previa del PDF de una factura")
            .WithDescription(PreviewText);

        canonical.MapPost("/calculate", Calculate)
            .Produces<DocumentTotalsCalculation>()
            .WithOperationId("calculateDocumentTotals")
            .WithSummary("Calcular los totales de un documento")
            .WithDescription(CalculateText)
            .WithRequestExamples(ExampleBodies.CalculateTotals);
    }

    /// <summary>
    /// Por qué el cálculo pide <c>documents:write</c> y no <c>documents:read</c>: es el primer paso de una emisión (el
    /// panel lo llama mientras arma el documento que va a emitir) y no consulta nada guardado; una credencial de solo
    /// consulta no emite, así que tampoco lo necesita.
    /// </summary>
    private const string CalculateText =
        "Calcula los valores de una factura, nota crédito o nota débito a partir de sus líneas: el valor de cada línea " +
        "(precio × cantidad − descuentos + cargos), sus impuestos, los impuestos del documento agrupados como en el XML " +
        "de la DIAN (un grupo por `tax_id` con un subtotal por tarifa) y `legal_monetary_totals`. Cada importe sale con " +
        "2 decimales, redondeado como se escribe en el XML, y los totales suman esos valores, así que lo calculado pasa " +
        "las reglas de totales de la emisión: el panel lo muestra y lo envía tal cual al emitir, sin hacer cuentas. En la " +
        "nota débito, `legal_monetary_totals` se envía como `requested_monetary_totals`. Los descuentos y cargos globales " +
        "no cambian los impuestos; los anticipos (`prepaid_payments`) suman `pre_paid_amount`, que no se resta del valor " +
        "a pagar. Es cálculo puro: no toma consecutivo, no guarda nada, no llama a la DIAN ni consume el plan. Pide " +
        "`documents:write`, porque es parte de emitir, y una credencial de empresa. Datos inválidos: 422 " +
        "(`validation.failed`, con `errors` por campo, por ejemplo `lines[0].taxes[0].percent`).";

    /// <summary>
    /// Por qué la vista previa pide <c>documents:write</c>: es parte de emitir (el botón «Vista previa» del formulario de
    /// emisión) y recibe los datos de un documento que todavía no existe.
    /// </summary>
    private const string PreviewText =
        "Genera el PDF que tendría la factura con el mismo cuerpo de `issueInvoice`, sin emitirla (el " +
        "`invoice/preeliminar-view` de ApiDian). Revisa lo mismo que la emisión, salvo lo que solo sirve para firmar y " +
        "enviar: forma del cuerpo, empresa activa (403 `company.inactive`), resolución activa del tipo y el prefijo, " +
        "catálogos y totales con las reglas de la DIAN (422 `validation.failed`, con `errors` por campo). No pide " +
        "certificado ni software, no toma número del consecutivo, no calcula el CUFE, no guarda nada, no consume el plan " +
        "y no llama a la DIAN; ignora `Idempotency-Key`. El PDF lleva la marca «VISTA PREVIA - SIN VALIDEZ», el número " +
        "`{prefijo} - BORRADOR`, «VISTA PREVIA - SIN CUFE» en lugar del CUFE, un QR de muestra y el logo de la empresa " +
        "si tiene. Responde `application/pdf` para abrir en el navegador (`Content-Disposition: inline`, " +
        "`Cache-Control: no-store`). Si el conversor de PDF no responde: 503 (`pdf.unavailable`, reintente después de " +
        "`Retry-After`). Pide `documents:write`.";

    private const string InvoiceText =
        "Emite una factura electrónica de venta. El cuerpo sigue el contrato de ApiDian: `customer` (comprador), " +
        "`invoice_lines`, `legal_monetary_totals`, `tax_totals`, `payment_form` y, si quiere, `number`, `prefix`, " +
        "`resolution_number`, `date` y `time` (hora de Colombia; sin ellas, la fecha y hora actuales). `payment_form` es " +
        "una forma de pago (objeto) o varias (lista); cada una va en el XML como un medio de pago, y sin `payment_form` se " +
        "usa contado en efectivo. Los anticipos ya " +
        "recibidos van en `prepaid_payment` (uno) o `prepaid_payments` (varios), cada uno con `idpayment`, `paidamount` y " +
        "`receiveddate` (`yyyy-MM-dd`) obligatorios y `paiddate` e `instructionid` opcionales; su suma va en " +
        "`legal_monetary_totals.pre_paid_amount` (obligatorio con `prepaid_payment`) y no se resta de `payable_amount`, " +
        "como lo pide la regla FAU14 de la DIAN.";

    private const string CreditNoteText =
        "Emite una nota crédito, que corrige o anula total o parcialmente una factura. El cuerpo sigue el contrato de " +
        "ApiDian: `billing_reference` (número, CUFE y fecha de la factura), `discrepancyresponsecode`, `customer`, " +
        "`credit_note_lines`, `legal_monetary_totals`, `tax_totals` y, si quiere, `payment_form` (una forma de pago o una " +
        "lista). Las notas no llevan anticipos.";

    private const string DebitNoteText =
        "Emite una nota débito, que agrega cargos a una factura. El cuerpo sigue el contrato de ApiDian: " +
        "`billing_reference` (número, CUFE y fecha de la factura), `discrepancyresponsecode`, `customer`, " +
        "`debit_note_lines`, `requested_monetary_totals`, `tax_totals` y, si quiere, `payment_form` (una forma de pago o " +
        "una lista). Las notas no llevan anticipos.";

    /// <summary>
    /// El cuerpo se lee a mano (su resumen identifica la solicitud repetida), así que su forma, sus ejemplos (los casos
    /// golden que Scalar muestra precargados) y la respuesta se declaran aquí para que aparezcan en el documento
    /// OpenAPI. El cuerpo se agrega solo al documento: con <c>Accepts</c> la ruta exigiría el tipo de contenido JSON y
    /// respondería 415 antes de revisar la credencial.
    /// </summary>
    /// <param name="producesPdf">Verdadero en la vista previa, que responde el PDF y no la respuesta de la emisión.</param>
    private static RouteHandlerBuilder Issue(RouteHandlerBuilder route, IReadOnlyList<RequestExample> examples, bool producesPdf = false) =>
        (producesPdf ? route.Produces(StatusCodes.Status200OK, contentType: "application/pdf") : route.Produces<IssueDocumentResponse>())
        .AddOpenApiOperationTransformer(async (operation, context, ct) => operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Schema = await context.GetOrCreateSchemaAsync(typeof(IssueDocumentBody), null, ct),
                    Examples = OpenApiExamples.ToOpenApi(examples),
                },
            },
        });

    private static async Task<IResult> Calculate(
        CalculateDocumentTotalsRequest body, IHandler<CalculateDocumentTotalsRequest, DocumentTotalsCalculation> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body, ct)).ToHttpResult();

    private static async Task<IResult> PreviewInvoice(
        HttpRequest request, HttpResponse response, IHandler<PreviewInvoiceQuery, byte[]> handler, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);
        if (!IssueDocumentJson.TryParse(buffer.ToArray(), out var body))
        {
            return IssueErrors.InvalidJson.ToHttpResult();
        }

        var result = await handler.HandleAsync(new PreviewInvoiceQuery(body!), ct);
        if (!result.IsSuccess)
        {
            return result.ToHttpResult();
        }

        // Se abre en el navegador, no se descarga, y no se guarda en ninguna caché: es un borrador.
        response.Headers.CacheControl = "no-store";
        response.Headers.ContentDisposition = "inline; filename=\"vista-previa.pdf\"";
        return TypedResults.File(result.Value, "application/pdf");
    }

    private static Task<IResult> Invoice(
        HttpRequest request, string? testSetId, IHandler<IssueInvoiceCommand, IssueDocumentResponse> handler, CancellationToken ct) =>
        IssueAsync(request, (body, raw, key) => handler.HandleAsync(new IssueInvoiceCommand(body, raw, key, testSetId), ct), ct);

    private static Task<IResult> CreditNote(
        HttpRequest request, string? testSetId, IHandler<IssueCreditNoteCommand, IssueDocumentResponse> handler, CancellationToken ct) =>
        IssueAsync(request, (body, raw, key) => handler.HandleAsync(new IssueCreditNoteCommand(body, raw, key, testSetId), ct), ct);

    private static Task<IResult> DebitNote(
        HttpRequest request, string? testSetId, IHandler<IssueDebitNoteCommand, IssueDocumentResponse> handler, CancellationToken ct) =>
        IssueAsync(request, (body, raw, key) => handler.HandleAsync(new IssueDebitNoteCommand(body, raw, key, testSetId), ct), ct);

    /// <summary>
    /// Lee el cuerpo completo tal como llegó (su resumen identifica la solicitud repetida) y lo interpreta con el
    /// contrato heredado; un cuerpo que no es JSON o no tiene su forma responde 422.
    /// </summary>
    private static async Task<IResult> IssueAsync(
        HttpRequest request,
        Func<IssueDocumentBody, byte[], string?, Task<Application.Abstractions.Results.Result<IssueDocumentResponse>>> issue,
        CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);
        var raw = buffer.ToArray();
        if (!IssueDocumentJson.TryParse(raw, out var body))
        {
            return IssueErrors.InvalidJson.ToHttpResult();
        }

        var key = request.Headers.TryGetValue(IdempotencyKeyHeader, out var values) ? values.ToString() : null;
        return (await issue(body!, raw, key)).ToHttpResult();
    }
}
