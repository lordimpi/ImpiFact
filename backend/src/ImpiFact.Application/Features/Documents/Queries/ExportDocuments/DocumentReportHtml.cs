using System.Globalization;
using System.Text;
using ImpiFact.Application.Abstractions.Documents;

namespace ImpiFact.Application.Features.Documents.Queries.ExportDocuments;

/// <summary>
/// HTML of the PDF report of the document list: landscape A4; a header with the company, the title «Documentos», the
/// filters in Spanish and the generation time in Colombia; the table with <see cref="DocumentExportColumns.Report"/>,
/// whose header row repeats on every page; and the document count. <see cref="Footer"/> numbers the pages.
/// </summary>
/// <remarks>
/// Every value is HTML-escaped (<see cref="Escape"/>): customer names and filters come from users. The page is
/// self-contained (inline CSS, no images or scripts), so the converter receives no other file.
/// </remarks>
public static class DocumentReportHtml
{
    /// <summary>
    /// Footer repeated on every page: «Página X de Y». The converter fills the <c>pageNumber</c> and <c>totalPages</c>
    /// classes; the footer is its own document, so it carries its own font size. Chromium ignores <c>text-align</c> on
    /// the footer body, so a flex row pushes the text to the right, and it lays the footer out at a different scale:
    /// 5mm of padding lands the text on the 10mm page margin (measured with Gotenberg 8.39).
    /// </summary>
    public const string Footer =
        """
        <!DOCTYPE html>
        <html lang="es-CO">
        <head><meta charset="utf-8"><style>
        body { width: 100%; margin: 0; font-family: Arial, Helvetica, sans-serif; font-size: 8pt; color: #444; }
        div { display: flex; justify-content: flex-end; width: 100%; padding: 0 5mm; box-sizing: border-box; }
        p { margin: 0; }
        </style></head>
        <body><div><p>Página <span class="pageNumber"></span> de <span class="totalPages"></span></p></div></body>
        </html>
        """;

    private const string All = "Todos";

    private const string DateFormat = "dd/MM/yyyy";

    private const string Styles =
        """
        @page { size: A4 landscape; margin: 12mm 10mm 16mm 10mm; }
        * { box-sizing: border-box; }
        body { margin: 0; font-family: Arial, Helvetica, sans-serif; font-size: 8pt; color: #1a1a1a; }
        header { display: flex; justify-content: space-between; align-items: flex-start; gap: 8mm; margin-bottom: 4mm; }
        .company { font-size: 9pt; }
        .company strong { display: block; font-size: 11pt; }
        h1 { margin: 0 0 1mm; font-size: 16pt; }
        .generated { margin: 0; color: #444; }
        dl { display: grid; grid-template-columns: max-content 1fr; gap: 0.5mm 3mm; margin: 0 0 3mm; }
        dt { font-weight: bold; }
        dd { margin: 0; overflow-wrap: anywhere; }
        table { width: 100%; border-collapse: collapse; }
        thead { display: table-header-group; }
        tr { break-inside: avoid; }
        th, td { padding: 1mm 1.2mm; border-bottom: 0.2mm solid #c8c8c8; text-align: left; vertical-align: top; overflow-wrap: anywhere; }
        th { background: #ececec; border-bottom: 0.3mm solid #8a8a8a; font-weight: bold; }
        .amount { text-align: right; white-space: nowrap; overflow-wrap: normal; }
        .nowrap { white-space: nowrap; overflow-wrap: normal; }
        .empty { padding: 4mm 1.2mm; text-align: center; color: #444; }
        .count { margin: 3mm 0 0; font-weight: bold; }
        """;

    /// <summary>Builds the report while it reads <paramref name="rows"/>, counting them for the last line.</summary>
    /// <param name="generatedAt">When the report is generated; shown in Colombia time.</param>
    public static async Task<string> BuildAsync(
        DocumentCompany company,
        IDocumentFilters filters,
        DateTimeOffset generatedAt,
        IAsyncEnumerable<DocumentSummary> rows,
        CancellationToken ct)
    {
        var html = new StringBuilder(16_384);
        html.Append("<!DOCTYPE html>\n<html lang=\"es-CO\">\n<head>\n<meta charset=\"utf-8\">\n<title>Documentos</title>\n<style>\n")
            .Append(Styles)
            .Append("\n</style>\n</head>\n<body>\n<header>\n<div>\n<h1>Documentos</h1>\n");
        var local = generatedAt.ToOffset(ColombiaDates.Offset);
        html.Append("<p class=\"generated\">Generado el ").Append(local.ToString(DateFormat, CultureInfo.InvariantCulture))
            .Append(" a las ").Append(local.ToString("HH:mm", CultureInfo.InvariantCulture)).Append(" (hora de Colombia)</p>\n</div>\n");
        html.Append("<div class=\"company\"><strong>").Append(Escape(company.Name)).Append("</strong>NIT ")
            .Append(Escape(company.IdentificationNumber)).Append("</div>\n</header>\n");
        AppendFilters(html, filters);
        AppendHeader(html);

        long count = 0;
        await foreach (var row in rows.WithCancellation(ct))
        {
            AppendRow(html, row);
            count++;
        }

        if (count == 0)
        {
            html.Append("<tr><td class=\"empty\" colspan=\"").Append(DocumentExportColumns.Report.Count.ToString(CultureInfo.InvariantCulture))
                .Append("\">No hay documentos con estos filtros.</td></tr>\n");
        }

        html.Append("</tbody>\n</table>\n<p class=\"count\">Total de documentos: ")
            .Append(count.ToString("N0", DocumentCsv.Numbers)).Append("</p>\n</body>\n</html>\n");
        return html.ToString();
    }

    /// <summary>Escapes the five characters with a meaning in HTML text and attribute values.</summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var escaped = new StringBuilder(value.Length + 16);
        foreach (var character in value)
        {
            _ = character switch
            {
                '&' => escaped.Append("&amp;"),
                '<' => escaped.Append("&lt;"),
                '>' => escaped.Append("&gt;"),
                '"' => escaped.Append("&quot;"),
                '\'' => escaped.Append("&#39;"),
                _ => escaped.Append(character),
            };
        }

        return escaped.ToString();
    }

    private static void AppendFilters(StringBuilder html, IDocumentFilters filters)
    {
        var typeId = filters.DocumentTypeId ?? DocumentKinds.TypeIdOf(filters.Kind);
        var statuses = DocumentFilters.ParseStatuses(filters.Status) ?? [];
        html.Append("<dl>\n");
        AppendFilter(html, "Fechas de emisión", DateRange(filters.From, filters.To));
        AppendFilter(html, "Tipo", typeId is { } id ? DocumentExportColumns.KindLabel(id) : All);
        AppendFilter(html, "Estados", statuses.Count == 0 ? All : string.Join(", ", statuses.Select(DocumentExportColumns.StatusLabel)));
        AppendFilter(html, "Cliente", DocumentFilters.Blank(filters.Customer) ? All : filters.Customer!.Trim());
        AppendFilter(html, "Número", DocumentFilters.Blank(filters.Number) ? All : filters.Number!.Trim());
        html.Append("</dl>\n");
    }

    private static void AppendFilter(StringBuilder html, string name, string value) =>
        html.Append("<dt>").Append(name).Append("</dt><dd>").Append(Escape(value)).Append("</dd>\n");

    private static string DateRange(string? from, string? to)
    {
        var hasFrom = ColombiaDates.TryParse(from, out var start);
        var hasTo = ColombiaDates.TryParse(to, out var end);
        return (hasFrom, hasTo) switch
        {
            (true, true) when start == end => Day(start),
            (true, true) => $"del {Day(start)} al {Day(end)}",
            (true, false) => $"desde el {Day(start)}",
            (false, true) => $"hasta el {Day(end)}",
            _ => All,
        };
    }

    private static string Day(DateOnly day) => day.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static void AppendHeader(StringBuilder html)
    {
        html.Append("<table>\n<thead><tr>");
        foreach (var column in DocumentExportColumns.Report)
        {
            html.Append(column.Kind == SpreadsheetCellKind.Amount ? "<th class=\"amount\">" : "<th>").Append(Escape(column.Header)).Append("</th>");
        }

        html.Append("</tr></thead>\n<tbody>\n");
    }

    private static void AppendRow(StringBuilder html, DocumentSummary row)
    {
        html.Append("<tr>");
        foreach (var column in DocumentExportColumns.Report)
        {
            var value = column.Value(row);
            var (cssClass, text) = value switch
            {
                decimal amount => ("amount", amount.ToString("N2", DocumentCsv.Numbers)),
                DateOnly date => ("nowrap", Day(date)),
                TimeOnly time => ("nowrap", time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
                _ when column.Header == "Número" => ("nowrap", Convert.ToString(value, CultureInfo.InvariantCulture)),
                _ => (null, Convert.ToString(value, CultureInfo.InvariantCulture)),
            };
            html.Append(cssClass is null ? "<td>" : $"<td class=\"{cssClass}\">").Append(Escape(text)).Append("</td>");
        }

        html.Append("</tr>\n");
    }
}
