using System.Text;
using ImpiFact.Application.Abstractions.Pdf;

namespace ImpiFact.Infrastructure.Pdf;

/// <summary>
/// <see cref="IPdfRenderer"/> con plantillas HTML (Handlebars) convertidas por Gotenberg (ADR-04): arma el HTML del
/// documento, dibuja el QR y manda a Gotenberg el HTML, la hoja de estilos, el QR y, si la empresa tiene, su logo.
/// </summary>
internal sealed class GotenbergPdfRenderer(DocumentHtmlTemplates templates, GotenbergClient gotenberg) : IPdfRenderer
{
    public Task<byte[]> RenderAsync(DocumentPdfModel model, CancellationToken ct)
    {
        List<GotenbergFile> files =
        [
            new GotenbergFile("index.html", Encoding.UTF8.GetBytes(templates.Render(model)), "text/html; charset=utf-8"),
            new GotenbergFile(DocumentHtmlTemplates.StylesheetFile, templates.Stylesheet, "text/css; charset=utf-8"),
            new GotenbergFile(DocumentHtmlTemplates.QrImageFile, QrCodeImage.Png(model.QrText), "image/png"),
        ];
        if (model.Logo is { } logo)
        {
            files.Add(new GotenbergFile(DocumentHtmlTemplates.LogoFileName(logo), logo.Content, logo.ContentType));
        }

        return gotenberg.ConvertHtmlAsync(files, ct);
    }
}
