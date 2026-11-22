using System.Globalization;
using System.IO.Compression;
using System.Text;
using ImpiFact.Application.Abstractions.Email;
using ImpiFact.Dian.Attached;

namespace ImpiFact.Application.Features.Documents.Email;

/// <summary>Datos del documento que muestra el correo.</summary>
/// <param name="CompanyName">Razón social de la empresa emisora.</param>
/// <param name="CustomerName">Nombre del comprador, de la copia del cliente en el documento.</param>
/// <param name="CustomerIdentification">Identificación (NIT u otra) del comprador.</param>
/// <param name="IssueDate">Fecha de emisión en hora de Colombia.</param>
/// <param name="IssueTime">Hora de emisión en hora de Colombia.</param>
/// <param name="Total">Valor a pagar del documento.</param>
/// <param name="DianQueryUrl">Dirección de consulta del documento en la DIAN, con su CUFE o CUDE al final.</param>
internal sealed record DocumentEmailContent(
    string CompanyName,
    string CustomerName,
    string CustomerIdentification,
    string Prefix,
    long Number,
    string FullNumber,
    DateOnly IssueDate,
    TimeOnly IssueTime,
    decimal Total,
    string DianQueryUrl);

/// <summary>
/// Asunto, adjunto y cuerpo del correo de un documento aceptado, como los arma ApiDian (la implementación de
/// referencia en PHP): <c>InvoiceMail</c>, <c>DocumentTrait::zipEmail</c> y la vista <c>mails/mail.blade.php</c>.
/// </summary>
/// <remarks>
/// Diferencias con el PHP: el ZIP no lleva anexos (<c>anx-*</c>), porque la emisión no los guarda todavía, y el cuerpo
/// no tiene el botón «Ver factura», que lleva al portal de clientes del PHP (ImpiFact no lo tiene). La fecha es la de
/// emisión del documento (el PHP muestra la de creación de la fila, que es casi la misma).
/// </remarks>
internal static class DocumentEmailComposer
{
    private const string ButtonStyle =
        "font-weight: bold; text-decoration: none; border: 0;padding: 10px 32px; color: #0880e8; border-radius: 50px; " +
        "border: 2px solid #0880e8; background: #fff; margin-bottom: 10px !important; text-align: center;";

    private const string CellStyle = "border: 1px solid #ddd; padding: 8px; border-radius: 5px;";

    /// <summary>Prefixes of the support-document family's XML names (<c>DianFileNames.Prefix</c>).</summary>
    private static readonly string[] SupportDocumentPrefixes = ["dse", "nds"];

    /// <summary>Prefixes of the XML names whose entry gives the attached-document name.</summary>
    private static readonly string[] EntryPrefixes = ["fv", "nc", "nd", "dse"];

    /// <summary>
    /// Asunto con el formato que exige la DIAN para el correo del documento electrónico y que arma el PHP:
    /// <c>NIT;nombre;prefijonúmero;código del tipo de documento;nombre</c>.
    /// </summary>
    public static string Subject(string companyNit, string companyName, string fullNumber, string documentTypeCode) =>
        $"{companyNit};{companyName};{fullNumber};{documentTypeCode};{companyName}";

    /// <summary>
    /// Nombre (sin extensión) del adjunto: el del XML que se envió a la DIAN dentro del ZIP con su prefijo
    /// <c>fv</c>, <c>nc</c> o <c>nd</c> cambiado por <c>ad</c> (el PHP toma el XmlFileName que devuelve la DIAN, que es
    /// ese mismo nombre), con <c>atacheddocument_name_prefix</c> delante si la solicitud lo trae.
    /// </summary>
    /// <param name="sentZip">Último ZIP enviado a la DIAN; si falta, el nombre sale del número del documento.</param>
    /// <remarks>
    /// The support document (<c>dse</c>) and its adjustment note (<c>nds</c>) get <c>ad</c> in place of their three-letter
    /// prefix: the PHP replaces <c>dse</c> and then <c>nd</c> and <c>ads</c> by <c>ad</c> (SupportDocumentController 364,
    /// SdCreditNoteController 330), which leaves the same name.
    /// </remarks>
    public static string AttachedDocumentName(string? prefix, byte[]? sentZip, string fullNumber)
    {
        var name = EntryName(sentZip) switch
        {
            { } entry when SupportDocumentPrefixes.Any(tag => entry.StartsWith(tag, StringComparison.Ordinal)) => string.Concat("ad", entry.AsSpan(3)),
            { } entry => AttachedDocumentFileName.FromDianXmlFileName(entry),
            null => "ad-" + fullNumber,
        };
        return (prefix ?? string.Empty) + name;
    }

    /// <summary>
    /// El ZIP <c>{nombre}.zip</c> con el AttachedDocument como <c>{nombre}.xml</c> y el PDF como <c>{nombre}.pdf</c>,
    /// como <c>zipEmail</c> del PHP.
    /// </summary>
    public static EmailAttachment Attachment(string name, byte[] attachedDocumentXml, byte[] pdf)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, name + ".xml", attachedDocumentXml);
            Add(archive, name + ".pdf", pdf);
        }

        return new EmailAttachment(name + ".zip", "application/zip", buffer.ToArray());
    }

    /// <summary>
    /// Cuerpo del correo en HTML y en texto, equivalente a <c>mails/mail.blade.php</c>. Si la solicitud trae su propio
    /// HTML (<c>html_header</c>, <c>html_body</c>, <c>html_buttons</c>, <c>html_footer</c>), esa parte va tal cual, como
    /// en el PHP; los datos del documento siempre se escapan.
    /// </summary>
    public static (string Html, string Text) Body(DocumentEmailContent content, DocumentEmailRequest request)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(request);
        var date = string.Create(CultureInfo.InvariantCulture, $"{content.IssueDate:yyyy-MM-dd} {content.IssueTime:HH:mm:ss}");
        var total = content.Total.ToString("N2", CultureInfo.InvariantCulture);
        var greeting = $"Señor(es), {content.CustomerName} identificado con NIT {content.CustomerIdentification}";
        var notice = $"Le informamos que ha recibido un documento electrónico de {content.CompanyName}.";
        const string attachedNotice = "Adjunto en este correo encontrará el PDF y el XML de su documento.";

        var html = new StringBuilder()
            .Append("<!doctype html>\n<html lang=\"es\">\n<head>\n<meta charset=\"UTF-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, user-scalable=no, initial-scale=1.0\">\n")
            .Append("<title>Notificación de Comprobante Electrónico Nro ").Append(Encode(content.Prefix)).Append('-')
            .Append(content.Number.ToString(CultureInfo.InvariantCulture)).Append("</title>\n</head>\n<body>\n")
            .Append("<div style=\"border-radius: 5px; border: solid 1px gray; padding: 20px;\">\n");

        html.Append(request.HtmlHeader ?? $"<p>{Encode(greeting)}</p>\n<p style=\"margin-bottom: 5px !important; margin-top: 5px !important;\">{Encode(notice)}</p>\n<p></p>\n");
        html.Append(request.HtmlBody ?? string.Concat(
            "<table style=\"width: 100%; text-align: center;\">\n<thead>\n<tr>\n",
            $"<th style=\"{CellStyle}\">Número de documento</th>\n<th style=\"{CellStyle}\">Fecha</th>\n<th style=\"{CellStyle}\">Valor</th>\n",
            "</tr>\n</thead>\n<tbody>\n<tr>\n",
            $"<td><p>{Encode(content.FullNumber)}</p></td>\n<td><p>{date}</p></td>\n<td><p>{total}</p></td>\n",
            "</tr>\n</tbody>\n</table>\n",
            "<p style=\"margin-bottom: 10px !important; padding-bottom: 10px !important\"> Adjunto en este correo encontrará el ",
            "<span style=\"color: red !important\">PDF</span> y <span style=\"color: #0880e8 !important\">XML</span> de su documento.</p>\n<p></p>\n"));
        if (request.HtmlButtons is not null)
        {
            html.Append(request.HtmlButtons);
        }

        html.Append($"<a style=\"{ButtonStyle}\" href=\"{Encode(content.DianQueryUrl)}\">Verificar en la DIAN</a>\n");
        if (request.HtmlFooter is not null)
        {
            html.Append(request.HtmlFooter);
        }

        html.Append("</div>\n</body>\n</html>\n");

        var text = string.Join('\n',
            greeting,
            notice,
            string.Empty,
            $"Número de documento: {content.FullNumber}",
            $"Fecha: {date}",
            $"Valor: {total}",
            string.Empty,
            attachedNotice,
            $"Verificar en la DIAN: {content.DianQueryUrl}",
            string.Empty);
        return (html.ToString(), text);
    }

    /// <summary>
    /// Escapa solo los caracteres especiales de HTML (<c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c> y las comillas); las
    /// letras con tilde y la ñ quedan tal cual porque el correo va en UTF-8.
    /// </summary>
    private static string Encode(string text) => new StringBuilder(text)
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;")
        .ToString();

    /// <summary>Nombre sin extensión de la única entrada del ZIP enviado a la DIAN; nulo si no se puede leer.</summary>
    private static string? EntryName(byte[]? zip)
    {
        if (zip is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            return archive.Entries is [var entry] && Path.GetFileNameWithoutExtension(entry.FullName) is { Length: > 2 } name
                && EntryPrefixes.Any(tag => name.StartsWith(tag, StringComparison.Ordinal))
                ? name
                : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static void Add(ZipArchive archive, string name, byte[] content)
    {
        using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
        stream.Write(content);
    }
}
