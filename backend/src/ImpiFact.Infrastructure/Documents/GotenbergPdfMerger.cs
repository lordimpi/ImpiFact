using ImpiFact.Application.Abstractions.Pdf;
using ImpiFact.Infrastructure.Pdf;

namespace ImpiFact.Infrastructure.Documents;

/// <summary>
/// <see cref="IPdfMerger"/> with the same Gotenberg client as the document PDFs (<see cref="GotenbergClient"/>, its address
/// and timeout from the «Pdf» section), through its PDF engines merge route.
/// </summary>
internal sealed class GotenbergPdfMerger(GotenbergClient gotenberg) : IPdfMerger
{
    public Task<byte[]> MergeAsync(IReadOnlyList<byte[]> pdfs, CancellationToken ct) => gotenberg.MergePdfsAsync(pdfs, ct);
}
