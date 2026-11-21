using System.Globalization;
using System.Text;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Queries.ExportDocuments;

/// <summary>
/// Formato del CSV (valores separados por punto y coma) de la exportación de documentos. Está pensado para abrirse con
/// doble clic en una hoja de cálculo configurada en español de Colombia:
/// <list type="bullet">
/// <item>Separador <c>;</c>, porque con la configuración regional es-CO la coma es el separador decimal.</item>
/// <item>Importes con coma decimal, dos decimales y sin separador de miles (<c>1234567,89</c>), para que la hoja los lea
/// como números.</item>
/// <item>Fechas <c>yyyy-MM-dd</c> y horas <c>HH:mm:ss</c> en hora Colombia, como en el XML.</item>
/// <item>UTF-8 con BOM (marca de orden de bytes) para que se vean bien las tildes, y fin de línea CRLF (RFC 4180).</item>
/// <item>Encabezados y estados en español.</item>
/// </list>
/// Un texto que empieza por <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c> o un tabulador lleva antes un apóstrofo, para que la
/// hoja no lo ejecute como fórmula (por ejemplo, un nombre de cliente malicioso).
/// </summary>
public static class DocumentCsv
{
    /// <summary>Números como en Colombia: coma decimal y punto de miles, sin depender de los datos regionales del sistema.</summary>
    public static readonly NumberFormatInfo Numbers = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NegativeSign = "-",
    };

    public const char Separator = ';';

    /// <summary>Encabezados en el orden de las columnas.</summary>
    public static IReadOnlyList<string> Headers { get; } = [.. DocumentExportColumns.All.Select(column => column.Header)];

    /// <summary>Escribe el encabezado y una línea por documento, a medida que llegan.</summary>
    public static async Task WriteAsync(Stream output, IAsyncEnumerable<DocumentSummary> rows, CancellationToken ct)
    {
        // Sin Dispose síncrono: el cuerpo de la respuesta HTTP no acepta escrituras síncronas.
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), bufferSize: 16 * 1024, leaveOpen: true)
        {
            NewLine = "\r\n",
        };
        await writer.WriteLineAsync(string.Join(Separator, Headers.Select(Cell)));
        await foreach (var row in rows.WithCancellation(ct))
        {
            await writer.WriteLineAsync(Line(row));
        }

        await writer.FlushAsync(ct);
    }

    /// <summary>Una línea del archivo, sin el fin de línea.</summary>
    public static string Line(DocumentSummary row) =>
        string.Join(Separator, DocumentExportColumns.All.Select(column => Format(column.Kind, column.Value(row))));

    public static string StatusLabel(DocumentStatus status) => DocumentExportColumns.StatusLabel(status);

    // CSV text of a typed value: dates and times as in the XML, amounts with a decimal comma, text escaped.
    private static string Format(SpreadsheetCellKind kind, object value) => kind switch
    {
        SpreadsheetCellKind.Date => ((DateOnly)value).ToString(ColombiaDates.Format, CultureInfo.InvariantCulture),
        SpreadsheetCellKind.Time => ((TimeOnly)value).ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        SpreadsheetCellKind.Amount => Amount((decimal)value),
        _ => Cell((string)value),
    };

    private static string Amount(decimal value) => value.ToString("0.00", Numbers);

    /// <summary>Texto de una celda: neutraliza fórmulas y va entre comillas si trae separador, comillas o saltos de línea.</summary>
    private static string Cell(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.AsSpan().IndexOfAny(";\"\r\n") >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }
}
