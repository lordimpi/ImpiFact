using System.Text;
using System.Text.RegularExpressions;
using ImpiFact.Application.Abstractions.Pdf;
using ImpiFact.Infrastructure.Pdf;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Pdf.Gotenberg;

/// <summary>
/// The merge of the PDFs of several documents (RF-12) against a real Gotenberg: its <c>/forms/pdfengines/merge</c> route
/// answers one PDF with the pages of every file, which the fake of the API tests cannot prove.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed partial class MergePdfsTests
{
    [Test]
    public async Task Gotenberg_merges_the_pdfs_into_one_with_the_pages_of_all()
    {
        await using var host = PdfHost.Create();
        await using var scope = host.OpenScope();
        var gotenberg = scope.ServiceProvider.GetRequiredService<GotenbergClient>();
        var first = await PageAsync(gotenberg, "Primera página");
        var second = await PageAsync(gotenberg, "Segunda página");

        var merged = await scope.ServiceProvider.GetRequiredService<IPdfMerger>().MergeAsync([first, second, first], CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(merged.Take(5), Is.EqualTo("%PDF-"u8.ToArray()));
            Assert.That(PageCount(first), Is.EqualTo(1), "Each converted file is one page.");
            Assert.That(PageCount(merged), Is.EqualTo(PageCount(first) * 3), "A page of each file, a repeated file included.");
        });
    }

    private static Task<byte[]> PageAsync(GotenbergClient gotenberg, string text) =>
        gotenberg.ConvertHtmlAsync(
            [new GotenbergFile("index.html", Encoding.UTF8.GetBytes($"<!doctype html><html><body><h1>{text}</h1></body></html>"), "text/html; charset=utf-8")],
            CancellationToken.None);

    /// <summary>Count of the page objects (<c>/Type /Page</c>, not <c>/Pages</c>) in the PDF.</summary>
    private static int PageCount(byte[] pdf) => PageObject().Count(Encoding.Latin1.GetString(pdf));

    [GeneratedRegex(@"/Type\s*/Page(?![a-zA-Z])")]
    private static partial Regex PageObject();
}
