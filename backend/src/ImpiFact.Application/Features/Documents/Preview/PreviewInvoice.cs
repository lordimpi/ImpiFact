using FluentValidation;
using FluentValidation.Results;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Pdf;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Application.Features.Documents.Pdf;
using ImpiFact.Application.Features.Documents.Totals;
using ImpiFact.Domain.Common;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Documents.Preview;

/// <summary>
/// <c>POST /api/v1/documents/invoices/preview</c>: el PDF que tendría la factura, antes de emitirla (el
/// <c>invoice/preeliminar-view</c> del PHP). No es un comando: no guarda nada, así que no abre transacción.
/// </summary>
/// <param name="Body">El mismo cuerpo de <c>issueInvoice</c>.</param>
public sealed record PreviewInvoiceQuery(IssueDocumentBody Body);

/// <summary>Las mismas reglas de forma de la emisión de la factura, con los mismos mensajes.</summary>
internal sealed class PreviewInvoiceQueryValidator : AbstractValidator<PreviewInvoiceQuery>
{
    public PreviewInvoiceQueryValidator() =>
        RuleFor(query => query).Custom((query, context) =>
            IssueDocumentRules.Validate(IssueSpec.Invoice, query.Body, (field, message) => context.AddFailure(new ValidationFailure(field, message))));
}

/// <summary>Errores propios de la vista previa.</summary>
public static class PreviewErrors
{
    /// <summary>Gotenberg (el conversor de HTML a PDF) no respondió, respondió con error o no devolvió un PDF.</summary>
    public static readonly Error PdfUnavailable = new(
        "pdf.unavailable",
        "No se pudo generar la vista previa del PDF en este momento. Intente de nuevo en unos segundos.",
        ErrorType.Unavailable);
}

/// <summary>
/// Revisa la factura como la emisión, sin las partes que solo sirven para firmar y enviar: empresa activa, resolución del
/// tipo y el prefijo, catálogos y totales con las reglas de la DIAN (Dirección de Impuestos y Aduanas Nacionales). Luego
/// arma el modelo imprimible con <see cref="DocumentPdfModelBuilder.BuildPreviewAsync"/> y lo convierte en PDF en la
/// misma solicitud.
/// </summary>
/// <remarks>
/// No pide certificado ni software (no firma), no revisa el plan (no emite), no toma número del consecutivo, no calcula
/// el CUFE (código único de la factura) y no llama a la DIAN. Una falla de Gotenberg responde 503
/// (<see cref="PreviewErrors.PdfUnavailable"/>); la cancelación de la solicitud por el cliente no se disfraza de 503.
/// </remarks>
internal sealed class PreviewInvoiceHandler(
    ITenantContext tenant,
    IIssuerContextReads issuers,
    IDocumentCatalogs catalogs,
    DocumentPdfModelBuilder builder,
    IPdfRenderer renderer,
    IClock clock,
    ILogger<PreviewInvoiceHandler> logger) : IHandler<PreviewInvoiceQuery, byte[]>
{
    public async Task<Result<byte[]>> HandleAsync(PreviewInvoiceQuery request, CancellationToken ct)
    {
        var companyId = tenant.CompanyId ?? throw new InvalidOperationException("The tenant decorator guarantees a company.");
        var spec = IssueSpec.Invoice;
        var body = request.Body;
        var issuer = await issuers.GetAsync(companyId, spec.DocumentTypeId, DocumentKind.Invoice, ct);
        if (issuer is null || !issuer.Company.IsActive)
        {
            return IssueErrors.CompanyInactive;
        }

        var resolution = IssueChecks.SelectResolution(spec, issuer, body);
        if (!resolution.IsSuccess)
        {
            return resolution.Error;
        }

        var found = await IssueChecks.LoadCatalogsAsync(spec, catalogs, issuer, body, ct);
        if (!found.IsSuccess)
        {
            return found.Error;
        }

        var totals = DianTotalsValidator.Validate(LegacyDocumentMapper.MapTotalsDocument(spec, body));
        if (!totals.IsSuccess)
        {
            return totals.Error;
        }

        var (issueDate, issueTime) = IssueChecks.IssueMoment(body, clock.UtcNow);
        var model = await builder.BuildPreviewAsync(issuer, resolution.Value, body, issueDate, issueTime, ct);
        try
        {
            return await renderer.RenderAsync(model, ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException
            || (exception is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(exception, "The PDF preview of company {CompanyId} could not be rendered.", companyId);
            return PreviewErrors.PdfUnavailable;
        }
    }
}
