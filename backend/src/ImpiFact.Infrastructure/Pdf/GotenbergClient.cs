using System.Net.Http.Headers;

namespace ImpiFact.Infrastructure.Pdf;

/// <summary>
/// Cliente de Gotenberg, el contenedor que convierte HTML en PDF con Chromium (licencia MIT). Usa la ruta
/// <c>POST /forms/chromium/convert/html</c>: un formulario <c>multipart</c> con <c>index.html</c> y los archivos que
/// el HTML pide por nombre (la hoja de estilos y la imagen del QR). Solo viaja el HTML del documento, nunca secretos.
/// Para unir PDF (RF-12) usa <c>POST /forms/pdfengines/merge</c> con los PDF guardados de los documentos.
/// </summary>
/// <remarks>
/// La dirección base y el plazo salen de <c>PdfOptions</c>. Una respuesta que no es un PDF lanza una excepción, y el
/// Worker reintenta el mensaje con su retroceso.
/// </remarks>
public sealed class GotenbergClient(HttpClient http)
{
    private const string ConvertHtmlPath = "forms/chromium/convert/html";

    private const string MergePath = "forms/pdfengines/merge";

    private const int MaxErrorLength = 500;

    /// <summary>Convierte <c>index.html</c> (que debe estar entre <paramref name="files"/>) en un PDF.</summary>
    /// <exception cref="HttpRequestException">Si Gotenberg no responde con éxito.</exception>
    /// <exception cref="InvalidDataException">Si la respuesta no es un PDF.</exception>
    public async Task<byte[]> ConvertHtmlAsync(IReadOnlyList<GotenbergFile> files, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        foreach (var file in files)
        {
            var part = new ByteArrayContent(file.Content);
            part.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
            form.Add(part, "files", file.Name);
        }

        // El tamaño de página y los márgenes los fija la regla @page de la hoja de estilos.
        form.Add(new StringContent("true"), "preferCssPageSize");
        form.Add(new StringContent("true"), "printBackground");

        return await PostAsync(ConvertHtmlPath, form, ct);
    }

    /// <summary>
    /// Merges <paramref name="pdfs"/> into one PDF with the route <c>POST /forms/pdfengines/merge</c>. Gotenberg merges the
    /// files in the alphabetical order of their names, so each one goes as <c>0001.pdf</c>, <c>0002.pdf</c>… in the order
    /// of the list.
    /// </summary>
    /// <exception cref="HttpRequestException">Si Gotenberg no responde con éxito.</exception>
    /// <exception cref="InvalidDataException">Si la respuesta no es un PDF.</exception>
    public async Task<byte[]> MergePdfsAsync(IReadOnlyList<byte[]> pdfs, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pdfs);
        using var form = new MultipartFormDataContent();
        for (var index = 0; index < pdfs.Count; index++)
        {
            var part = new ByteArrayContent(pdfs[index]);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(part, "files", string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{index + 1:D4}.pdf"));
        }

        return await PostAsync(MergePath, form, ct);
    }

    private async Task<byte[]> PostAsync(string path, MultipartFormDataContent form, CancellationToken ct)
    {
        using var response = await http.PostAsync(path, form, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Gotenberg answered {(int)response.StatusCode}: {(detail.Length > MaxErrorLength ? detail[..MaxErrorLength] : detail)}",
                inner: null,
                response.StatusCode);
        }

        var pdf = await response.Content.ReadAsByteArrayAsync(ct);
        return pdf.AsSpan().StartsWith("%PDF"u8) ? pdf : throw new InvalidDataException("Gotenberg did not answer with a PDF.");
    }
}

/// <summary>Un archivo del formulario de Gotenberg.</summary>
/// <param name="Name">Nombre con que lo pide el HTML, por ejemplo <c>styles.css</c>.</param>
public sealed record GotenbergFile(string Name, byte[] Content, string ContentType);
