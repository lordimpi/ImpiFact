namespace ImpiFact.Application.Abstractions.Pdf;

/// <summary>
/// Converts a self-contained HTML page into a PDF. Used for reports built in Application, such as the PDF export of the
/// document list; the PDF of an electronic document goes through <see cref="IPdfRenderer"/> instead. Infrastructure
/// implements it with Gotenberg, the same HTML-to-PDF converter of the document PDFs (ADR-04).
/// </summary>
/// <remarks>
/// When the converter does not answer, answers an error or answers something that is not a PDF, the call throws
/// <see cref="HttpRequestException"/> or <see cref="InvalidDataException"/>, as <see cref="IPdfRenderer"/> does.
/// </remarks>
public interface IHtmlPdfConverter
{
    /// <summary>Bytes of the PDF of <paramref name="page"/>.</summary>
    Task<byte[]> ConvertAsync(HtmlPdfPage page, CancellationToken ct);
}

/// <summary>A page to convert. Its styles are inline: the converter receives no other file.</summary>
/// <param name="Html">Complete HTML document; its CSS <c>@page</c> rule sets the paper size, orientation and margins.</param>
/// <param name="FooterHtml">
/// Optional complete HTML document repeated in the bottom margin of every page. An element with the class
/// <c>pageNumber</c> shows the page number and one with <c>totalPages</c> the page count.
/// </param>
public sealed record HtmlPdfPage(string Html, string? FooterHtml = null);
