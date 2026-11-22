namespace ImpiFact.Application.Abstractions.Pdf;

/// <summary>
/// Merges several PDFs into one, keeping the order of the list (RF-12 «unir PDFs»). Infrastructure implements it with the
/// PDF engines of Gotenberg, the same converter of the document PDFs (ADR-04), so no PDF library enters the code.
/// </summary>
/// <remarks>
/// When the converter does not answer, answers an error or answers something that is not a PDF, the call throws
/// <see cref="HttpRequestException"/> or <see cref="InvalidDataException"/>, as <see cref="IHtmlPdfConverter"/> does.
/// </remarks>
public interface IPdfMerger
{
    /// <summary>Bytes of one PDF with the pages of <paramref name="pdfs"/>, in their order.</summary>
    Task<byte[]> MergeAsync(IReadOnlyList<byte[]> pdfs, CancellationToken ct);
}
