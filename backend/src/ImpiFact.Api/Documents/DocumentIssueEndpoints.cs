using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Documents.Issue;

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

    private const string Tag = "Documents";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var legacy = endpoints.MapGroup("/api/ubl2.1").WithTags(Tag).RequireAuthorization(ApiScopes.DocumentsWrite);
        legacy.MapPost("/invoice", Invoice);
        legacy.MapPost("/invoice/{testSetId}", Invoice);
        legacy.MapPost("/credit-note", CreditNote);
        legacy.MapPost("/credit-note/{testSetId}", CreditNote);
        legacy.MapPost("/debit-note", DebitNote);
        legacy.MapPost("/debit-note/{testSetId}", DebitNote);

        var canonical = endpoints.MapGroup("/api/v1/documents").WithTags(Tag).RequireAuthorization(ApiScopes.DocumentsWrite);
        canonical.MapPost("/invoices", Invoice);
        canonical.MapPost("/invoices/test-set/{testSetId}", Invoice);
        canonical.MapPost("/credit-notes", CreditNote);
        canonical.MapPost("/credit-notes/test-set/{testSetId}", CreditNote);
        canonical.MapPost("/debit-notes", DebitNote);
        canonical.MapPost("/debit-notes/test-set/{testSetId}", DebitNote);
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
