using System.IO.Compression;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using ImpiFact.Application.Abstractions.Documents;

namespace ImpiFact.Infrastructure.Documents;

/// <summary>
/// Writes a single-sheet .xlsx workbook with the OpenXML SDK. The SDK's package model keeps every part in memory until
/// it is saved (about 55 MB for 50,000 rows), so this writer packs the parts itself with a <see cref="ZipArchive"/> in
/// create mode and streams each part with <see cref="OpenXmlWriter"/>: memory stays flat whatever the row count. The
/// ZIP goes to a temporary file that is deleted on close, because the zip and the SDK write synchronously and the HTTP
/// response body only accepts asynchronous writes; the finished file is then copied to the output.
/// </summary>
internal sealed class OpenXmlSpreadsheetWriter : ISpreadsheetWriter
{
    private const uint HeaderStyle = 1;
    private const uint AmountStyle = 2;
    private const uint DateStyle = 3;
    private const uint TimeStyle = 4;

    public async Task WriteAsync(
        Stream output,
        string sheetName,
        IReadOnlyList<SpreadsheetColumn> columns,
        IAsyncEnumerable<object?[]> rows,
        CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), $"impifact-export-{Guid.NewGuid():N}.xlsx");
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.DeleteOnClose);
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteText(zip, "[Content_Types].xml", ContentTypes);
            WriteText(zip, "_rels/.rels", PackageRelationships);
            WriteText(zip, "xl/_rels/workbook.xml.rels", WorkbookRelationships);
            WritePart(zip, "xl/workbook.xml", new Workbook(new Sheets(new Sheet { Name = sheetName, SheetId = 1, Id = "rId1" })));
            WritePart(zip, "xl/styles.xml", Styles());
            await WriteSheetAsync(zip, columns, rows, ct);
        }

        file.Position = 0;
        await file.CopyToAsync(output, ct);
    }

    private static async Task WriteSheetAsync(ZipArchive zip, IReadOnlyList<SpreadsheetColumn> columns, IAsyncEnumerable<object?[]> rows, CancellationToken ct)
    {
        await using var part = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest).Open();
        using var writer = OpenXmlWriter.Create(part);
        writer.WriteStartDocument();
        writer.WriteStartElement(new Worksheet());

        writer.WriteStartElement(new Columns());
        for (var i = 0; i < columns.Count; i++)
        {
            writer.WriteElement(new Column { Min = (uint)i + 1, Max = (uint)i + 1, Width = columns[i].Width, CustomWidth = true });
        }

        writer.WriteEndElement();

        writer.WriteStartElement(new SheetData());
        uint rowIndex = 1;
        writer.WriteStartElement(new Row { RowIndex = rowIndex });
        for (var i = 0; i < columns.Count; i++)
        {
            writer.WriteElement(TextCell(Reference(i, rowIndex), columns[i].Header, HeaderStyle));
        }

        writer.WriteEndElement();

        await foreach (var values in rows.WithCancellation(ct))
        {
            rowIndex++;
            writer.WriteStartElement(new Row { RowIndex = rowIndex });
            for (var i = 0; i < columns.Count; i++)
            {
                if (ValueCell(Reference(i, rowIndex), columns[i].Kind, values[i]) is { } cell)
                {
                    writer.WriteElement(cell);
                }
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static Cell? ValueCell(string reference, SpreadsheetCellKind kind, object? value) => (kind, value) switch
    {
        (_, null) => null,
        (SpreadsheetCellKind.Amount, decimal amount) => new Cell { CellReference = reference, StyleIndex = AmountStyle, CellValue = new CellValue(amount) },
        (SpreadsheetCellKind.Date, DateOnly date) =>
            new Cell { CellReference = reference, StyleIndex = DateStyle, CellValue = new CellValue(date.ToDateTime(TimeOnly.MinValue).ToOADate()) },
        (SpreadsheetCellKind.Time, TimeOnly time) =>
            new Cell { CellReference = reference, StyleIndex = TimeStyle, CellValue = new CellValue(time.ToTimeSpan().TotalDays) },
        (_, string { Length: 0 }) => null,
        (_, string text) => TextCell(reference, text, styleIndex: null),
        _ => throw new ArgumentException($"A {kind} cell cannot hold a {value.GetType().Name}.", nameof(value)),
    };

    // Inline strings: no shared-string table to keep in memory, and a text cell is never evaluated as a formula.
    private static Cell TextCell(string reference, string text, uint? styleIndex) => new()
    {
        CellReference = reference,
        DataType = CellValues.InlineString,
        StyleIndex = styleIndex,
        InlineString = new InlineString(new Text(XmlSafe(text)) { Space = SpaceProcessingModeValues.Preserve }),
    };

    /// <summary>Drops characters XML 1.0 cannot carry (control characters in a customer name, for example).</summary>
    private static string XmlSafe(string text)
    {
        if (!text.Any(character => !XmlConvert.IsXmlChar(character) && !char.IsSurrogate(character)))
        {
            return text;
        }

        var safe = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (XmlConvert.IsXmlChar(character) || char.IsSurrogate(character))
            {
                safe.Append(character);
            }
        }

        return safe.ToString();
    }

    /// <summary>A1-style reference of a zero-based column and a one-based row.</summary>
    private static string Reference(int column, uint row)
    {
        var name = string.Empty;
        for (var index = column + 1; index > 0; index = (index - 1) / 26)
        {
            name = (char)('A' + ((index - 1) % 26)) + name;
        }

        return name + row.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Styles, by index: 0 default, 1 bold header, 2 amount with thousands separator and two decimals, 3 date
    /// dd/mm/yyyy and 4 time hh:mm:ss. Number format codes are locale-neutral: the spreadsheet shows them with the
    /// separators of the reader's regional settings.
    /// </summary>
    private static Stylesheet Styles() => new(
        new NumberingFormats(
            new NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0.00" },
            new NumberingFormat { NumberFormatId = 165, FormatCode = "dd/mm/yyyy" },
            new NumberingFormat { NumberFormatId = 166, FormatCode = "hh:mm:ss" })
        { Count = 3 },
        new Fonts(
            new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }, new FontFamilyNumbering { Val = 2 }),
            new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" }, new FontFamilyNumbering { Val = 2 }))
        { Count = 2 },
        new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }))
        { Count = 2 },
        new Borders(new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder())) { Count = 1 },
        new CellStyleFormats(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }) { Count = 1 },
        new CellFormats(
            new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 },
            new CellFormat { NumberFormatId = 0, FontId = 1, FillId = 0, BorderId = 0, FormatId = 0, ApplyFont = true },
            new CellFormat { NumberFormatId = 164, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = 165, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = 166, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0, ApplyNumberFormat = true })
        { Count = 5 },
        new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });

    private static void WritePart(ZipArchive zip, string name, OpenXmlElement root)
    {
        using var part = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
        using var writer = OpenXmlWriter.Create(part);
        writer.WriteStartDocument();
        writer.WriteElement(root);
    }

    private static void WriteText(ZipArchive zip, string name, string content)
    {
        using var part = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
        using var writer = new StreamWriter(part, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private const string ContentTypes =
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>"""
        + """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">"""
        + """<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>"""
        + """<Default Extension="xml" ContentType="application/xml"/>"""
        + """<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>"""
        + """<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>"""
        + """<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>"""
        + "</Types>";

    private const string PackageRelationships =
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>"""
        + """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">"""
        + """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>"""
        + "</Relationships>";

    private const string WorkbookRelationships =
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>"""
        + """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">"""
        + """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>"""
        + """<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>"""
        + "</Relationships>";
}
