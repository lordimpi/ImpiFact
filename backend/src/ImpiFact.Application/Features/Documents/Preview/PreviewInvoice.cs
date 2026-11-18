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

/// <summary>El cuerpo de una vista previa: el mismo de la emisión del tipo.</summary>
public interface IPreviewDocumentQuery
{
    IssueDocumentBody Body { get; }
}

/// <summary>
/// <c>POST /api/v1/documents/invoices/preview</c>: el PDF que tendría la factura, antes de emitirla (el
/// <c>invoice/preeliminar-view</c> del PHP). No es un comando: no guarda nada, así que no abre transacción.
/// </summary>
/// <param name="Body">El mismo cuerpo de <c>issueInvoice</c>.</param>
public sealed record PreviewInvoiceQuery(IssueDocumentBody Body) : IPreviewDocumentQuery;

/// <summary><c>POST /api/v1/documents/credit-notes/preview</c>: el PDF que tendría la nota crédito, antes de emitirla.</summary>
/// <param name="Body">El mismo cuerpo de <c>issueCreditNote</c>.</param>
public sealed record PreviewCreditNoteQuery(IssueDocumentBody Body) : IPreviewDocumentQuery;

/// <summary><c>POST /api/v1/documents/debit-notes/preview</c>: el PDF que tendría la nota débito, antes de emitirla.</summary>
/// <param name="Body">El mismo cuerpo de <c>issueDebitNote</c>.</param>
public sealed record PreviewDebitNoteQuery(IssueDocumentBody Body) : IPreviewDocumentQuery;

/// <summary>Las mismas reglas de forma de la emisión del tipo, con los mismos mensajes.</summary>
internal abstract class PreviewQueryValidator<TQuery> : AbstractValidator<TQuery>
    where TQuery : IPreviewDocumentQuery
{
    protected PreviewQueryValidator(IssueSpec spec) =>
        RuleFor(query => query).Custom((query, context) =>
            IssueDocumentRules.Validate(spec, query.Body, (field, message) => context.AddFailure(new ValidationFailure(field, message))));
}

internal sealed class PreviewInvoiceQueryValidator() : PreviewQueryValidator<PreviewInvoiceQuery>(IssueSpec.Invoice);

internal sealed class PreviewCreditNoteQueryValidator() : PreviewQueryValidator<PreviewCreditNoteQuery>(IssueSpec.CreditNote);

internal sealed class PreviewDebitNoteQueryValidator() : PreviewQueryValidator<PreviewDebitNoteQuery>(IssueSpec.DebitNote);

/// <summary>Errores propios de la vista previa.</summary>
public static class PreviewErrors
{
    /// <summary>Gotenberg (el conversor de HTML a PDF) no respondió, respondió con error o no devolvió un PDF.</summary>
    public static readonly Error PdfUnavailable = new(
        "pdf.unavailable",
        "No se pudo generar la vista previa del PDF en este momento. Intente de nuevo en unos segundos.",
        ErrorType.Unavailable);
}

/// <summary>Puertos que usan las tres vistas previas; cada handler los recibe por inyección y los pasa juntos.</summary>
internal sealed record PreviewPorts(
    ITenantContext Tenant,
    IIssuerContextReads Issuers,
    IDocumentCatalogs Catalogs,
    DocumentPdfModelBuilder Builder,
    IPdfRenderer Renderer,
    IClock Clock,
    ILogger Logger);

/// <summary>
/// Revisa el documento como la emisión, sin las partes que solo sirven para firmar y enviar: empresa activa, resolución
/// del tipo y el prefijo, catálogos y totales con las reglas de la DIAN (Dirección de Impuestos y Aduanas Nacionales).
/// Luego arma el modelo imprimible con <see cref="DocumentPdfModelBuilder.BuildPreviewAsync"/> y lo convierte en PDF en
/// la misma solicitud.
/// </summary>
/// <remarks>
/// No pide certificado ni software (no firma), no revisa el plan (no emite), no toma número del consecutivo, no calcula
/// el CUFE ni el CUDE (códigos únicos de la factura y de la nota) y no llama a la DIAN. En las notas tampoco revisa que
/// la factura de <c>billing_reference</c> esté aceptada: una nota se prepara mientras la factura espera respuesta, y esa
/// regla se revisa al emitir. Una falla de Gotenberg responde 503 (<see cref="PreviewErrors.PdfUnavailable"/>); la
/// cancelación de la solicitud por el cliente no se disfraza de 503.
/// </remarks>
internal abstract class PreviewDocumentHandler<TQuery>(PreviewPorts ports, IssueSpec spec) : IHandler<TQuery, byte[]>
    where TQuery : IPreviewDocumentQuery
{
    public async Task<Result<byte[]>> HandleAsync(TQuery request, CancellationToken ct)
    {
        var companyId = ports.Tenant.CompanyId ?? throw new InvalidOperationException("The tenant decorator guarantees a company.");
        var body = request.Body;
        var issuer = await ports.Issuers.GetAsync(companyId, spec.DocumentTypeId, DocumentKind.Invoice, ct);
        if (issuer is null || !issuer.Company.IsActive)
        {
            return IssueErrors.CompanyInactive;
        }

        var resolution = IssueChecks.SelectResolution(spec, issuer, body);
        if (!resolution.IsSuccess)
        {
            return resolution.Error;
        }

        var found = await IssueChecks.LoadCatalogsAsync(spec, ports.Catalogs, issuer, body, ct);
        if (!found.IsSuccess)
        {
            return found.Error;
        }

        var totals = DianTotalsValidator.Validate(LegacyDocumentMapper.MapTotalsDocument(spec, body));
        if (!totals.IsSuccess)
        {
            return totals.Error;
        }

        var (issueDate, issueTime) = IssueChecks.IssueMoment(body, ports.Clock.UtcNow);
        var model = await ports.Builder.BuildPreviewAsync(spec, issuer, resolution.Value, body, issueDate, issueTime, ct);
        try
        {
            return await ports.Renderer.RenderAsync(model, ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException
            || (exception is OperationCanceledException && !ct.IsCancellationRequested))
        {
            ports.Logger.LogWarning(exception, "The PDF preview ({Kind}) of company {CompanyId} could not be rendered.", spec.Kind, companyId);
            return PreviewErrors.PdfUnavailable;
        }
    }
}

internal sealed class PreviewInvoiceHandler(
    ITenantContext tenant, IIssuerContextReads issuers, IDocumentCatalogs catalogs, DocumentPdfModelBuilder builder, IPdfRenderer renderer,
    IClock clock, ILogger<PreviewInvoiceHandler> logger)
    : PreviewDocumentHandler<PreviewInvoiceQuery>(new PreviewPorts(tenant, issuers, catalogs, builder, renderer, clock, logger), IssueSpec.Invoice);

internal sealed class PreviewCreditNoteHandler(
    ITenantContext tenant, IIssuerContextReads issuers, IDocumentCatalogs catalogs, DocumentPdfModelBuilder builder, IPdfRenderer renderer,
    IClock clock, ILogger<PreviewCreditNoteHandler> logger)
    : PreviewDocumentHandler<PreviewCreditNoteQuery>(new PreviewPorts(tenant, issuers, catalogs, builder, renderer, clock, logger), IssueSpec.CreditNote);

internal sealed class PreviewDebitNoteHandler(
    ITenantContext tenant, IIssuerContextReads issuers, IDocumentCatalogs catalogs, DocumentPdfModelBuilder builder, IPdfRenderer renderer,
    IClock clock, ILogger<PreviewDebitNoteHandler> logger)
    : PreviewDocumentHandler<PreviewDebitNoteQuery>(new PreviewPorts(tenant, issuers, catalogs, builder, renderer, clock, logger), IssueSpec.DebitNote);
