using System.Globalization;
using System.Net;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Documents;

namespace ImpiFact.Integration.Tests.DocumentQueries;

/// <summary>
/// Excel export of the list (<c>GET /api/v1/documents/export.xlsx</c>): the same filters, rows and columns as the CSV,
/// with a bold header, real numbers and dates, and the same errors.
/// </summary>
[Category("Integration")]
public sealed class DocumentXlsxExportApiTests
{
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly DateTimeOffset Start = new(2026, 5, 4, 15, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task The_workbook_has_the_csv_headers_in_bold_typed_cells_and_only_the_filtered_rows_of_the_company()
    {
        var company = await DocumentSeed.CompanyAsync();
        var other = await DocumentSeed.CompanyAsync();
        await DocumentSeed.DocumentAsync(company, 1, 1, DocumentStatus.Accepted, Start, cufe: "cufe-xlsx-1-" + company.Nit,
            customerName: "Acme \"La Mejor\"", customerIdentification: "900123456", emailStatus: EmailStatus.Sent);
        await DocumentSeed.DocumentAsync(company, 4, 2, DocumentStatus.Rejected, Start.AddMinutes(1), customerName: "=HYPERLINK(\"x\")");
        await DocumentSeed.DocumentAsync(company, 1, 3, DocumentStatus.Queued, Start.AddMinutes(2));
        await DocumentSeed.DocumentAsync(other, 1, 1, DocumentStatus.Accepted, Start, customerName: "Otra empresa");
        using var client = await DocumentSeed.ClientAsync(company);

        using var response = await client.GetAsync("/api/v1/documents/export.xlsx?status=accepted,rejected&from=2026-05-04&to=2026-05-04");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var workbook = Workbook.Open(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo(XlsxType));
            Assert.That(response.Content.Headers.ContentDisposition!.FileName?.Trim('"'), Is.EqualTo("documentos.xlsx"));
            Assert.That(workbook.Errors, Is.Empty, "The package must be a valid OpenXML spreadsheet.");
            Assert.That(workbook.SheetNames, Is.EqualTo(new[] { "Documentos" }));
            Assert.That(workbook.Texts(1), Is.EqualTo(new[]
            {
                "Tipo", "Número", "Fecha de emisión", "Hora de emisión", "Identificación del cliente", "Cliente", "Subtotal",
                "Impuestos", "Total", "Estado", "Estado del correo", "CUFE/CUDE",
            }));
            Assert.That(workbook.IsBold("A1"), Is.True, "Bold header.");
            Assert.That(workbook.IsBold("A2"), Is.False);
            Assert.That(workbook.RowCount, Is.EqualTo(3), "Header plus the two rows that match the filters.");
            Assert.That(workbook.Texts(2), Is.EqualTo(new[]
            {
                "Nota crédito", "SETP2", null, null, "222222222222", "=HYPERLINK(\"x\")", null, null, null, "Rechazado", "No enviado", null,
            }), "Text cells are never formulas, so they need no apostrophe.");
            Assert.That(workbook.Texts(3)[5], Is.EqualTo("Acme \"La Mejor\""));
            Assert.That(workbook.Texts(3)[11], Is.EqualTo("cufe-xlsx-1-" + company.Nit));
            Assert.That(workbook.Texts(3)[9], Is.EqualTo("Aceptado"));
            Assert.That(workbook.Number("G3"), Is.EqualTo(100.00m));
            Assert.That(workbook.Number("H3"), Is.EqualTo(19.00m));
            Assert.That(workbook.Number("I3"), Is.EqualTo(119.00m));
            Assert.That(workbook.FormatOf("I3"), Is.EqualTo("#,##0.00"));
            Assert.That(DateTime.FromOADate((double)workbook.Number("C3")), Is.EqualTo(new DateTime(2026, 5, 4)));
            Assert.That(workbook.FormatOf("C3"), Is.EqualTo("dd/mm/yyyy"));
            Assert.That(DateTime.FromOADate((double)workbook.Number("D3")).TimeOfDay, Is.EqualTo(new TimeSpan(10, 0, 0)));
            Assert.That(workbook.FormatOf("D3"), Is.EqualTo("hh:mm:ss"));
            Assert.That(workbook.Widths, Has.Count.EqualTo(12).And.All.GreaterThanOrEqualTo(10));
            Assert.That(workbook.AllTexts, Has.None.EqualTo("Otra empresa"), "Another company's documents are not exported.");
        });
    }

    [Test]
    public async Task No_matching_documents_give_a_workbook_with_only_the_header()
    {
        var company = await DocumentSeed.CompanyAsync();
        using var client = await DocumentSeed.ClientAsync(company);

        using var response = await client.GetAsync("/api/v1/documents/export.xlsx");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var workbook = Workbook.Open(await response.Content.ReadAsByteArrayAsync());
        Assert.Multiple(() =>
        {
            Assert.That(workbook.Errors, Is.Empty);
            Assert.That(workbook.RowCount, Is.EqualTo(1));
            Assert.That(workbook.Texts(1)[0], Is.EqualTo("Tipo"));
        });
    }

    [Test]
    public async Task More_rows_than_the_maximum_answer_422_before_writing_anything()
    {
        var company = await DocumentSeed.CompanyAsync();
        await DocumentSeed.ManyDocumentsAsync(company, 50_001, Start);
        using var client = await DocumentSeed.ClientAsync(company);

        using var tooMany = await client.GetAsync("/api/v1/documents/export.xlsx");
        using var narrowed = await client.GetAsync("/api/v1/documents/export.xlsx?number=BULK7");

        using var json = await DocumentSeed.JsonAsync(tooMany);
        Assert.Multiple(() =>
        {
            Assert.That(tooMany.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(json.RootElement.GetProperty("code").GetString(), Is.EqualTo("documents.export_too_large"));
            Assert.That(narrowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
        using var workbook = Workbook.Open(await narrowed.Content.ReadAsByteArrayAsync());
        Assert.That(workbook.RowCount, Is.EqualTo(2));
    }

    [Test]
    public async Task The_export_needs_documents_read_and_rejects_invalid_filters()
    {
        var company = await DocumentSeed.CompanyAsync();
        using var configOnly = await DocumentSeed.ClientAsync(company, ApiScopes.ConfigWrite);
        using var reader = await DocumentSeed.ClientAsync(company);
        using var anonymous = DocumentQueriesHost.Api.CreateClient();

        using var forbidden = await configOnly.GetAsync("/api/v1/documents/export.xlsx");
        using var unauthenticated = await anonymous.GetAsync("/api/v1/documents/export.xlsx");
        using var invalid = await reader.GetAsync("/api/v1/documents/export.xlsx?kind=receipt&from=ayer");

        using var json = await DocumentSeed.JsonAsync(invalid);
        Assert.Multiple(() =>
        {
            Assert.That(forbidden.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(unauthenticated.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(json.RootElement.GetProperty("errors").GetProperty("kind")[0].GetString(), Is.EqualTo("kind es inválido."));
            Assert.That(json.RootElement.GetProperty("errors").GetProperty("from")[0].GetString(), Is.EqualTo("from no es una fecha válida."));
        });
    }

    [Test]
    public async Task Control_characters_are_dropped_and_spaces_kept_so_the_workbook_stays_valid()
    {
        var columns = new[] { new SpreadsheetColumn("Cliente", SpreadsheetCellKind.Text, 20) };
        using var output = new MemoryStream();

        await new OpenXmlSpreadsheetWriter().WriteAsync(output, "Documentos", columns, Rows(["\u0001Acme\u0002 SAS "]), CancellationToken.None);

        using var workbook = Workbook.Open(output.ToArray());
        Assert.Multiple(() =>
        {
            Assert.That(workbook.Errors, Is.Empty);
            Assert.That(workbook.Texts(2)[0], Is.EqualTo("Acme SAS "));
        });
    }

    private static async IAsyncEnumerable<object?[]> Rows(params object?[][] rows)
    {
        foreach (var row in rows)
        {
            await Task.Yield();
            yield return row;
        }
    }

    /// <summary>Reads the downloaded workbook with the OpenXML SDK, an implementation independent of the writer.</summary>
    private sealed class Workbook : IDisposable
    {
        private readonly MemoryStream _stream;
        private readonly SpreadsheetDocument _document;
        private readonly Worksheet _sheet;
        private readonly Stylesheet _styles;

        private Workbook(byte[] bytes)
        {
            _stream = new MemoryStream(bytes);
            _document = SpreadsheetDocument.Open(_stream, isEditable: false);
            var part = _document.WorkbookPart!;
            _sheet = part.WorksheetParts.Single().Worksheet!;
            _styles = part.WorkbookStylesPart!.Stylesheet!;
            SheetNames = [.. part.Workbook!.Sheets!.Elements<Sheet>().Select(sheet => sheet.Name!.Value!)];
            Errors = [.. new OpenXmlValidator(FileFormatVersions.Office2010).Validate(_document).Select(error => error.Description)];
        }

        public IReadOnlyList<string> SheetNames { get; }

        public IReadOnlyList<string> Errors { get; }

        public int RowCount => Rows.Count();

        public IReadOnlyList<double> Widths => [.. _sheet.Descendants<Column>().Select(column => column.Width!.Value)];

        public IEnumerable<string> AllTexts => Rows.SelectMany(row => row.Elements<Cell>()).Select(Text).OfType<string>();

        private IEnumerable<Row> Rows => _sheet.Descendants<Row>();

        public static Workbook Open(byte[] bytes) => new(bytes);

        /// <summary>Text of the 12 cells of a row (1-based), null where the cell is not text.</summary>
        public string?[] Texts(int row)
        {
            var texts = new string?[12];
            foreach (var cell in Rows.Single(candidate => candidate.RowIndex!.Value == row).Elements<Cell>())
            {
                texts[ColumnIndex(cell.CellReference!.Value!)] = Text(cell);
            }

            return texts;
        }

        public decimal Number(string reference)
        {
            var cell = Cell(reference);
            Assert.That(cell.DataType?.Value, Is.Null.Or.EqualTo(CellValues.Number), $"{reference} is a number.");
            return decimal.Parse(cell.CellValue!.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        public string? FormatOf(string reference)
        {
            var format = CellFormat(reference);
            return _styles.NumberingFormats?.Elements<NumberingFormat>()
                .SingleOrDefault(candidate => candidate.NumberFormatId!.Value == format.NumberFormatId!.Value)?.FormatCode?.Value;
        }

        public bool IsBold(string reference)
        {
            var font = _styles.Fonts!.Elements<Font>().ElementAt((int)(CellFormat(reference).FontId?.Value ?? 0));
            return font.Bold is not null && (font.Bold.Val is null || font.Bold.Val.Value);
        }

        public void Dispose()
        {
            _document.Dispose();
            _stream.Dispose();
        }

        private static string? Text(Cell cell) =>
            cell.DataType?.Value == CellValues.InlineString ? cell.InlineString!.InnerText
            : cell.DataType?.Value == CellValues.String ? cell.CellValue?.Text
            : null;

        private static int ColumnIndex(string reference) => reference[0] - 'A';

        private Cell Cell(string reference) => _sheet.Descendants<Cell>().Single(cell => cell.CellReference?.Value == reference);

        private CellFormat CellFormat(string reference) =>
            _styles.CellFormats!.Elements<CellFormat>().ElementAt((int)(Cell(reference).StyleIndex?.Value ?? 0));
    }
}
