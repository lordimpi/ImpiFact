using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Pdf;
using ImpiFact.Application.Abstractions.Results;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Documents.Queries.ExportDocuments;

/// <summary>
/// <c>GET /api/v1/documents/export.pdf</c>: the documents of the list with the same filters as the CSV and Excel exports,
/// as a PDF report to print or share (<see cref="DocumentReportHtml"/>), converted by Gotenberg.
/// </summary>
public sealed record ExportDocumentsPdfQuery(
    int? DocumentTypeId,
    string? Kind,
    string? Status,
    string? From,
    string? To,
    string? Customer,
    string? Number) : IDocumentFilters
{
    /// <summary>
    /// Row cap of the PDF report, lower than the CSV and Excel cap (<see cref="ExportDocumentsQuery.MaxRows"/>): the
    /// whole report is built in memory and converted in one request, and 5,000 rows are already 130 to 280 landscape
    /// pages (20 to 38 rows a page, depending on how many customer names wrap). For more rows the client downloads Excel
    /// or narrows the filters.
    /// </summary>
    public const int MaxRows = 5_000;

    public const string ContentType = "application/pdf";

    /// <summary>Suggested name of the downloaded file.</summary>
    public const string FileName = "documentos.pdf";
}

/// <summary>
/// Counts the rows with the shared export row source (<see cref="DocumentExportRows"/>): over <see cref="ExportDocumentsPdfQuery.MaxRows"/>
/// it answers 422 before reading them. Otherwise it reads the rows, builds the report and converts it. A converter failure
/// answers 503 (<see cref="DocumentErrors.PdfUnavailable"/>), as the document preview does; the client's own cancellation
/// is not disguised as a 503.
/// </summary>
internal sealed class ExportDocumentsPdfHandler(
    IDocumentReads reads, ITenantContext tenant, IHtmlPdfConverter converter, IClock clock, ILogger<ExportDocumentsPdfHandler> logger)
    : IHandler<ExportDocumentsPdfQuery, byte[]>
{
    private const int MaxRows = ExportDocumentsPdfQuery.MaxRows;

    public async Task<Result<byte[]>> HandleAsync(ExportDocumentsPdfQuery request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId || await reads.FindCompanyAsync(companyId, ct) is not { } company)
        {
            return DocumentErrors.ForeignNit;
        }

        var (filter, count) = await DocumentExportRows.CountAsync(reads, request, companyId, MaxRows, ct);
        if (count > MaxRows)
        {
            return DocumentErrors.PdfExportTooLarge(count, MaxRows);
        }

        var html = await DocumentReportHtml.BuildAsync(company, request, clock.UtcNow, reads.StreamAsync(filter, ct), ct);
        try
        {
            return await converter.ConvertAsync(new HtmlPdfPage(html, DocumentReportHtml.Footer), ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException
            || (exception is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(exception, "The PDF export of the documents of company {CompanyId} could not be rendered.", companyId);
            return DocumentErrors.PdfUnavailable;
        }
    }
}

internal sealed class ExportDocumentsPdfQueryValidator : AbstractValidator<ExportDocumentsPdfQuery>
{
    public ExportDocumentsPdfQueryValidator() => Include(new DocumentFiltersValidator());
}
