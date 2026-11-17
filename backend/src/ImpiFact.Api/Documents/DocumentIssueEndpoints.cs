using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Documents.Issue;
using Microsoft.OpenApi;

namespace ImpiFact.Api.Documents;

/// <summary>
/// Emisión de factura, nota crédito y nota débito (Arquitectura detallada, sección 7; anexo A.1 a A.3). Todas piden el
/// alcance <c>documents:write</c>; la empresa es siempre la de la credencial, porque el cuerpo heredado no trae el NIT
/// del emisor. Las rutas heredadas <c>/api/ubl2.1/...</c> y sus variantes con <c>{testSetId}</c> (set de pruebas de
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
        legacy.MapPost("/invoice", Invoice);
        legacy.MapPost("/invoice/{testSetId}", Invoice);
        legacy.MapPost("/credit-note", CreditNote);
        legacy.MapPost("/credit-note/{testSetId}", CreditNote);
        legacy.MapPost("/debit-note", DebitNote);
        legacy.MapPost("/debit-note/{testSetId}", DebitNote);

        var canonical = endpoints.MapGroup("/api/v1/documents").WithTags(ApiTags.Documents).RequireAuthorization(ApiScopes.DocumentsWrite);
        Issue(canonical.MapPost("/invoices", Invoice), OpenApiExamples.Invoices)
            .WithSummary("Emitir una factura")
            .WithDescription(InvoiceText + IssueRules);
        Issue(canonical.MapPost("/invoices/test-set/{testSetId}", Invoice), OpenApiExamples.Invoices)
            .WithSummary("Emitir una factura del set de pruebas")
            .WithDescription(InvoiceText + TestSetRules + IssueRules);
        Issue(canonical.MapPost("/credit-notes", CreditNote), OpenApiExamples.CreditNotes)
            .WithSummary("Emitir una nota crédito")
            .WithDescription(CreditNoteText + NoteRules + IssueRules);
        Issue(canonical.MapPost("/credit-notes/test-set/{testSetId}", CreditNote), OpenApiExamples.CreditNotes)
            .WithSummary("Emitir una nota crédito del set de pruebas")
            .WithDescription(CreditNoteText + TestSetRules + IssueRules);
        Issue(canonical.MapPost("/debit-notes", DebitNote), OpenApiExamples.DebitNotes)
            .WithSummary("Emitir una nota débito")
            .WithDescription(DebitNoteText + NoteRules + IssueRules);
        Issue(canonical.MapPost("/debit-notes/test-set/{testSetId}", DebitNote), OpenApiExamples.DebitNotes)
            .WithSummary("Emitir una nota débito del set de pruebas")
            .WithDescription(DebitNoteText + TestSetRules + IssueRules);
    }

    private const string InvoiceText =
        "Emite una factura electrónica de venta. El cuerpo sigue el contrato de ApiDian: `customer` (comprador), " +
        "`invoice_lines`, `legal_monetary_totals`, `tax_totals`, `payment_form` y, si quiere, `number`, `prefix`, " +
        "`resolution_number`, `date` y `time` (hora de Colombia; sin ellas, la fecha y hora actuales).";

    private const string CreditNoteText =
        "Emite una nota crédito, que corrige o anula total o parcialmente una factura. El cuerpo sigue el contrato de " +
        "ApiDian: `billing_reference` (número, CUFE y fecha de la factura), `discrepancyresponsecode`, `customer`, " +
        "`credit_note_lines`, `legal_monetary_totals` y `tax_totals`.";

    private const string DebitNoteText =
        "Emite una nota débito, que agrega cargos a una factura. El cuerpo sigue el contrato de ApiDian: " +
        "`billing_reference` (número, CUFE y fecha de la factura), `discrepancyresponsecode`, `customer`, " +
        "`debit_note_lines`, `requested_monetary_totals` y `tax_totals`.";

    /// <summary>
    /// El cuerpo se lee a mano (su resumen identifica la solicitud repetida), así que su forma, sus ejemplos (los casos
    /// golden que Scalar muestra precargados) y la respuesta se declaran aquí para que aparezcan en el documento
    /// OpenAPI. El cuerpo se agrega solo al documento: con <c>Accepts</c> la ruta exigiría el tipo de contenido JSON y
    /// respondería 415 antes de revisar la credencial.
    /// </summary>
    private static RouteHandlerBuilder Issue(RouteHandlerBuilder route, IReadOnlyList<RequestExample> examples) => route
        .Produces<IssueDocumentResponse>()
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
