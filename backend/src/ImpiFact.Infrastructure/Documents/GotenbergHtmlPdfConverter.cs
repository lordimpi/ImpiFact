using System.Text;
using ImpiFact.Application.Abstractions.Pdf;
using ImpiFact.Infrastructure.Pdf;

namespace ImpiFact.Infrastructure.Documents;

/// <summary>
/// <see cref="IHtmlPdfConverter"/> with the same Gotenberg client as the document PDFs (<see cref="GotenbergClient"/>,
/// its address and timeout from the «Pdf» section). Sends <c>index.html</c> and, when the page has one, <c>footer.html</c>,
/// which Gotenberg repeats at the bottom of every page and where it fills the page number and count.
/// </summary>
internal sealed class GotenbergHtmlPdfConverter(GotenbergClient gotenberg) : IHtmlPdfConverter
{
    private const string HtmlType = "text/html; charset=utf-8";

    public Task<byte[]> ConvertAsync(HtmlPdfPage page, CancellationToken ct)
    {
        List<GotenbergFile> files = [new GotenbergFile("index.html", Encoding.UTF8.GetBytes(page.Html), HtmlType)];
        if (page.FooterHtml is { } footer)
        {
            files.Add(new GotenbergFile("footer.html", Encoding.UTF8.GetBytes(footer), HtmlType));
        }

        return gotenberg.ConvertHtmlAsync(files, ct);
    }
}
