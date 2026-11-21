using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Pdf;
using ImpiFact.Application.Features.Documents.Queries.ExportDocuments;
using ImpiFact.Application.Tests.Features.Companies;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImpiFact.Application.Tests.Features.Documents;

/// <summary>PDF report of the document list: its own row cap, the shared filters and the HTML handed to the converter.</summary>
public sealed class DocumentPdfExportTests
{
    /// <summary>15:07 UTC is 10:07 in Colombia (UTC-5).</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 15, 7, 0, TimeSpan.Zero);

    private FakeDocumentReads _reads = null!;
    private RecordingConverter _converter = null!;
    private Guid _companyId;

    [SetUp]
    public void SetUp()
    {
        _companyId = Guid.CreateVersion7();
        _reads = new FakeDocumentReads();
        _reads.Companies.Add(new DocumentCompany(_companyId, "900123456", "Panadería <La Espiga> & Cía SAS"));
        _converter = new RecordingConverter();
    }

    [Test]
    public async Task More_rows_than_the_pdf_maximum_are_rejected_without_reading_or_rendering_them()
    {
        _reads.Total = ExportDocumentsPdfQuery.MaxRows + 1;

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(ExportDocumentsPdfQuery.MaxRows, Is.EqualTo(5_000));
            Assert.That(result.Error.Code, Is.EqualTo("documents.export_too_large"));
            Assert.That(result.Error.Type, Is.EqualTo(Abstractions.Results.ErrorType.Validation));
            Assert.That(result.Error.Message, Is.EqualTo(
                "El PDF tendría 5.001 documentos y el máximo es 5.000. Descárguelos en Excel o acote los filtros, por ejemplo con un rango de fechas más corto."));
            Assert.That(_reads.StreamCalls, Is.Empty);
            Assert.That(_converter.Pages, Is.Empty);
        });
    }

    [Test]
    public async Task Exactly_the_pdf_maximum_is_rendered_with_the_same_filters_as_the_list()
    {
        _reads.Total = ExportDocumentsPdfQuery.MaxRows;

        var result = await Handler().HandleAsync(Query(kind: "debit_note", status: "accepted"), CancellationToken.None);

        var filter = _reads.StreamCalls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(RecordingConverter.Pdf));
            Assert.That(filter.CompanyId, Is.EqualTo(_companyId));
            Assert.That(filter.DocumentTypeId, Is.EqualTo(5));
            Assert.That(filter.Statuses, Is.EqualTo(new[] { DocumentStatus.Accepted }));
            Assert.That(filter.After, Is.Null);
            Assert.That(filter.Take, Is.EqualTo(ExportDocumentsPdfQuery.MaxRows));
            Assert.That(_reads.CountListCalls.Single(), Is.EqualTo(filter));
            Assert.That(_converter.Pages, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task The_report_has_the_company_the_filters_in_spanish_the_colombian_time_and_a_landscape_repeated_header()
    {
        _reads.Listed.Add(DocumentSamples.Summary(typeId: 5, number: 12, status: DocumentStatus.AcceptedWithWarnings) with
        {
            Totals = new DocumentTotals(1234567.5m, 1234567.5m, 1469135.33m, 0m, 0m, 0m, 1469135.33m),
            EmailStatus = EmailStatus.Failed,
        });

        await Handler().HandleAsync(
            new ExportDocumentsPdfQuery(null, "debit_note", "accepted,rejected", "2026-05-04", "2026-05-31", " acme ", "SETP12"), CancellationToken.None);

        var page = _converter.Pages.Single();
        Assert.Multiple(() =>
        {
            Assert.That(page.Html, Does.StartWith("<!DOCTYPE html>"));
            Assert.That(page.Html, Does.Contain("<html lang=\"es-CO\">"));
            Assert.That(page.Html, Does.Contain("<meta charset=\"utf-8\">"));
            Assert.That(page.Html, Does.Contain("size: A4 landscape"));
            Assert.That(page.Html, Does.Contain("thead { display: table-header-group; }"));
            Assert.That(page.Html, Does.Contain("Panadería &lt;La Espiga&gt; &amp; Cía SAS"), "The business name is escaped.");
            Assert.That(page.Html, Does.Contain("NIT 900123456"));
            Assert.That(page.Html, Does.Contain("<h1>Documentos</h1>"));
            Assert.That(page.Html, Does.Contain("<dt>Fechas de emisión</dt><dd>del 04/05/2026 al 31/05/2026</dd>"));
            Assert.That(page.Html, Does.Contain("<dt>Tipo</dt><dd>Nota débito</dd>"));
            Assert.That(page.Html, Does.Contain("<dt>Estados</dt><dd>Aceptado, Rechazado</dd>"));
            Assert.That(page.Html, Does.Contain("<dt>Cliente</dt><dd>acme</dd>"));
            Assert.That(page.Html, Does.Contain("<dt>Número</dt><dd>SETP12</dd>"));
            Assert.That(page.Html, Does.Contain("Generado el 21/11/2026 a las 10:07 (hora de Colombia)"));
            Assert.That(page.Html, Does.Contain("<p class=\"count\">Total de documentos: 1</p>"));
            Assert.That(page.FooterHtml, Does.Contain("Página <span class=\"pageNumber\"></span> de <span class=\"totalPages\"></span>"));
        });
    }

    [Test]
    public async Task The_table_has_the_readable_subset_of_the_export_columns_with_colombian_amounts_aligned_right()
    {
        _reads.Listed.Add(DocumentSamples.Summary(typeId: 5, number: 12, status: DocumentStatus.AcceptedWithWarnings) with
        {
            Totals = new DocumentTotals(1234567.5m, 1234567.5m, 1469135.33m, 0m, 0m, 0m, 1469135.33m),
            EmailStatus = EmailStatus.Failed,
        });

        await Handler().HandleAsync(Query(), CancellationToken.None);

        var html = _converter.Pages.Single().Html;
        Assert.Multiple(() =>
        {
            Assert.That(DocumentExportColumns.Report.Select(column => column.Header), Is.EqualTo(new[]
            {
                "Tipo", "Número", "Fecha de emisión", "Hora de emisión", "Identificación del cliente", "Cliente", "Subtotal",
                "Impuestos", "Total", "Estado", "Estado del correo",
            }), "Every column except the 96-character CUFE/CUDE, which does not fit a landscape page.");
            Assert.That(html, Does.Contain(
                "<thead><tr><th>Tipo</th><th>Número</th><th>Fecha de emisión</th><th>Hora de emisión</th><th>Identificación del cliente</th>"
                + "<th>Cliente</th><th class=\"amount\">Subtotal</th><th class=\"amount\">Impuestos</th><th class=\"amount\">Total</th>"
                + "<th>Estado</th><th>Estado del correo</th></tr></thead>"));
            Assert.That(html, Does.Contain(
                "<tr><td>Nota débito</td><td class=\"nowrap\">SETP12</td><td class=\"nowrap\">15/01/2026</td><td class=\"nowrap\">10:30:45</td>"
                + "<td>222222222222</td><td>Consumidor final</td><td class=\"amount\">1.234.567,50</td><td class=\"amount\">234.567,83</td>"
                + "<td class=\"amount\">1.469.135,33</td><td>Aceptado con advertencias</td><td>Falló</td></tr>"));
            Assert.That(html, Does.Not.Contain("cufe12"));
            Assert.That(html, Does.Contain(".amount { text-align: right;"));
        });
    }

    [Test]
    public async Task Without_filters_every_filter_reads_todos_and_an_empty_result_says_so()
    {
        await Handler().HandleAsync(Query(), CancellationToken.None);

        var html = _converter.Pages.Single().Html;
        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<dt>Fechas de emisión</dt><dd>Todos</dd>"));
            Assert.That(html, Does.Contain("<dt>Tipo</dt><dd>Todos</dd>"));
            Assert.That(html, Does.Contain("<dt>Estados</dt><dd>Todos</dd>"));
            Assert.That(html, Does.Contain("<dt>Cliente</dt><dd>Todos</dd>"));
            Assert.That(html, Does.Contain("<dt>Número</dt><dd>Todos</dd>"));
            Assert.That(html, Does.Contain("No hay documentos con estos filtros."));
            Assert.That(html, Does.Contain("<p class=\"count\">Total de documentos: 0</p>"));
        });
    }

    [TestCase("2026-05-04", null, "desde el 04/05/2026")]
    [TestCase(null, "2026-05-31", "hasta el 31/05/2026")]
    [TestCase("2026-05-04", "2026-05-04", "04/05/2026")]
    public async Task One_sided_and_single_day_ranges_read_naturally(string? from, string? to, string expected)
    {
        await Handler().HandleAsync(new ExportDocumentsPdfQuery(1, null, null, from, to, null, null), CancellationToken.None);

        var html = _converter.Pages.Single().Html;
        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain($"<dt>Fechas de emisión</dt><dd>{expected}</dd>"));
            Assert.That(html, Does.Contain("<dt>Tipo</dt><dd>Factura</dd>"), "document_type_id names the type as well as kind.");
        });
    }

    [Test]
    public async Task Customer_values_are_escaped_so_they_never_become_markup()
    {
        _reads.Listed.Add(DocumentSamples.Summary() with
        {
            Customer = new DocumentCustomer(3, "1\"2'3", null, "<script>alert('x')</script> & Hijos", null, null, null, null, null, null, null, null),
        });

        await Handler().HandleAsync(new ExportDocumentsPdfQuery(null, null, null, null, null, "<b>", null), CancellationToken.None);

        var html = _converter.Pages.Single().Html;
        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<td>&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; &amp; Hijos</td>"));
            Assert.That(html, Does.Contain("<td>1&quot;2&#39;3</td>"));
            Assert.That(html, Does.Contain("<dt>Cliente</dt><dd>&lt;b&gt;</dd>"));
            Assert.That(html, Does.Not.Contain("<script>"));
            Assert.That(html, Does.Not.Contain("<b>"));
        });
    }

    [Test]
    public async Task A_failed_conversion_answers_pdf_unavailable()
    {
        _converter.Failure = new HttpRequestException("Gotenberg is down.");

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error.Code, Is.EqualTo("pdf.unavailable"));
            Assert.That(result.Error.Type, Is.EqualTo(Abstractions.Results.ErrorType.Unavailable));
            Assert.That(result.Error.Message, Is.EqualTo("No se pudo generar el PDF en este momento. Intente de nuevo en unos segundos."));
        });
    }

    [Test]
    public void The_file_is_named_documentos_pdf()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ExportDocumentsPdfQuery.FileName, Is.EqualTo("documentos.pdf"));
            Assert.That(ExportDocumentsPdfQuery.ContentType, Is.EqualTo("application/pdf"));
        });
    }

    private static ExportDocumentsPdfQuery Query(string? kind = null, string? status = null) => new(null, kind, status, null, null, null, null);

    private ExportDocumentsPdfHandler Handler() =>
        new(_reads, new FakeDocumentTenant(_companyId), _converter, new FakeClock(Now), NullLogger<ExportDocumentsPdfHandler>.Instance);

    /// <summary>Records the pages handed to the HTML-to-PDF port and answers a minimal PDF.</summary>
    private sealed class RecordingConverter : IHtmlPdfConverter
    {
        public static readonly byte[] Pdf = "%PDF-1.7\n%%EOF\n"u8.ToArray();

        public List<HtmlPdfPage> Pages { get; } = [];

        public Exception? Failure { get; set; }

        public Task<byte[]> ConvertAsync(HtmlPdfPage page, CancellationToken ct)
        {
            Pages.Add(page);
            return Failure is null ? Task.FromResult(Pdf) : Task.FromException<byte[]>(Failure);
        }
    }
}
