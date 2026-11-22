using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Pdf;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Documents.Queries.MergePdfs;

/// <summary>
/// <c>POST /api/v1/documents/pdf/merge</c> (RF-12 «unir PDFs», the <c>join-pdfs</c> of ApiDian): the latest PDF of each
/// document of the company, in the order of <see cref="DocumentIds"/>, merged into one PDF.
/// </summary>
public sealed record MergeDocumentPdfsQuery
{
    /// <summary>Most documents of one merge: every PDF is read into memory and sent to the converter in one request.</summary>
    public const int MaxDocuments = 50;

    public const string ContentType = "application/pdf";

    /// <summary>Suggested name of the downloaded file.</summary>
    public const string FileName = "documentos-unidos.pdf";

    /// <summary>Ids of the documents, in the order their pages go; a repeated id repeats its pages.</summary>
    [JsonPropertyName("document_ids")]
    public IReadOnlyList<Guid>? DocumentIds { get; init; }
}

/// <summary>Errors of the merge.</summary>
internal static class MergePdfErrors
{
    public static string Missing(IEnumerable<string> fullNumbers) =>
        $"Estos documentos no tienen PDF: {string.Join(", ", fullNumbers)}.";

    /// <summary>Some documents have no PDF yet (the Worker renders it after the DIAN accepts the document).</summary>
    public static Error PdfMissing(IReadOnlyList<string> fullNumbers)
    {
        var message = Missing(fullNumbers);
        return new Error("documents.pdf_missing", message, ErrorType.Validation) { Fields = [new FieldError("document_ids", message)] };
    }
}

internal sealed class MergeDocumentPdfsQueryValidator : AbstractValidator<MergeDocumentPdfsQuery>
{
    public const string CountMessage = "Envíe entre 1 y 50 documentos para unir sus PDF.";

    public MergeDocumentPdfsQueryValidator()
    {
        RuleFor(query => query.DocumentIds)
            .Must(ids => ids is { Count: > 0 and <= MergeDocumentPdfsQuery.MaxDocuments }).WithMessage(CountMessage)
            .OverridePropertyName("document_ids");
    }
}

/// <summary>
/// Looks up every document in the company of the credential: one that does not exist there (also one of another company)
/// answers 404 for the whole request, without saying which. Then reads the latest PDF of each one: the documents without a
/// PDF answer 422 naming their numbers. A single document answers its own PDF; more go to the converter
/// (<see cref="IPdfMerger"/>), whose failure answers 503 (<see cref="DocumentErrors.PdfUnavailable"/>) as the PDF export.
/// </summary>
internal sealed class MergeDocumentPdfsHandler(
    IDocumentLookups lookups, IDocumentFileReads files, ITenantContext tenant, IPdfMerger merger, ILogger<MergeDocumentPdfsHandler> logger)
    : IHandler<MergeDocumentPdfsQuery, byte[]>
{
    public async Task<Result<byte[]>> HandleAsync(MergeDocumentPdfsQuery request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
        {
            return DocumentErrors.NotFound;
        }

        var documents = new List<DocumentRef>(request.DocumentIds!.Count);
        foreach (var id in request.DocumentIds)
        {
            if (await lookups.FindByIdAsync(companyId, id, ct) is not { } document)
            {
                return DocumentErrors.NotFound;
            }

            documents.Add(document);
        }

        var pdfs = new List<byte[]>(documents.Count);
        var missing = new List<string>();
        foreach (var document in documents)
        {
            if (await files.ReadLatestAsync(companyId, document.Id, DocumentFileKind.Pdf, ct) is { } pdf)
            {
                pdfs.Add(pdf);
            }
            else if (!missing.Contains(document.FullNumber, StringComparer.Ordinal))
            {
                missing.Add(document.FullNumber);
            }
        }

        if (missing.Count > 0)
        {
            return MergePdfErrors.PdfMissing(missing);
        }

        if (pdfs.Count == 1)
        {
            return pdfs[0];
        }

        try
        {
            return await merger.MergeAsync(pdfs, ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException
            || (exception is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(exception, "The PDFs of {Count} documents of company {CompanyId} could not be merged.", pdfs.Count, companyId);
            return DocumentErrors.PdfUnavailable;
        }
    }
}
