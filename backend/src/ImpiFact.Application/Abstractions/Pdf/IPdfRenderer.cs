namespace ImpiFact.Application.Abstractions.Pdf;

/// <summary>
/// Genera la representación gráfica (el PDF que recibe el comprador) de un documento electrónico a partir de su modelo
/// imprimible (<see cref="DocumentPdfModel"/>). No sabe con qué motor se arma el PDF: Infrastructure lo implementa con
/// plantillas HTML y un conversor de HTML a PDF (ADR-04).
/// </summary>
public interface IPdfRenderer
{
    /// <summary>Bytes del PDF del documento.</summary>
    Task<byte[]> RenderAsync(DocumentPdfModel model, CancellationToken ct);
}
