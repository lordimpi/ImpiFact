using System.Text;
using ImpiFact.Application.Abstractions.Pdf;

namespace ImpiFact.Infrastructure.Pdf;

/// <summary>
/// <see cref="IPdfRenderer"/> con plantillas HTML (Handlebars) convertidas por Gotenberg (ADR-04): arma el HTML del
/// documento, dibuja el QR y manda los tres archivos a Gotenberg.
/// </summary>
internal sealed class GotenbergPdfRenderer(DocumentHtmlTemplates templates, GotenbergClient gotenberg) : IPdfRenderer
{
    public Task<byte[]> RenderAsync(DocumentPdfModel model, CancellationToken ct) => gotenberg.ConvertHtmlAsync(
        [
            new GotenbergFile("index.html", Encoding.UTF8.GetBytes(templates.Render(model)), "text/html; charset=utf-8"),
            new GotenbergFile(DocumentHtmlTemplates.StylesheetFile, templates.Stylesheet, "text/css; charset=utf-8"),
            new GotenbergFile(DocumentHtmlTemplates.QrImageFile, QrCodeImage.Png(model.QrText), "image/png"),
        ],
        ct);
}
