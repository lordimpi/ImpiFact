using System.Text;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Features.Documents.Queries;
using ImpiFact.Application.Features.Documents.Queries.ExportDocuments;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Tests.Features.Documents;

/// <summary>Exportación CSV del listado: tope de filas, filtros compartidos y formato del archivo.</summary>
public sealed class DocumentExportTests
{
    private FakeDocumentReads _reads = null!;
    private Guid _companyId;

    [SetUp]
    public void SetUp()
    {
        _companyId = Guid.CreateVersion7();
        _reads = new FakeDocumentReads();
    }

    [Test]
    public async Task More_rows_than_the_maximum_are_rejected_without_reading_them()
    {
        _reads.Total = ExportDocumentsQuery.MaxRows + 1;

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error.Code, Is.EqualTo("documents.export_too_large"));
            Assert.That(result.Error.Message, Is.EqualTo(
                "La exportación tiene 50.001 documentos y el máximo es 50.000. Acote los filtros, por ejemplo con un rango de fechas más corto."));
            Assert.That(_reads.StreamCalls, Is.Empty);
        });
    }

    [Test]
    public async Task Exactly_the_maximum_is_exported_with_the_same_filters_as_the_list()
    {
        _reads.Total = ExportDocumentsQuery.MaxRows;

        var result = await Handler().HandleAsync(Query(kind: "debit_note", status: "accepted"), CancellationToken.None);
        await result.Value.WriteCsvAsync(Stream.Null, CancellationToken.None);

        var filter = _reads.StreamCalls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Count, Is.EqualTo(ExportDocumentsQuery.MaxRows));
            Assert.That(filter.CompanyId, Is.EqualTo(_companyId));
            Assert.That(filter.DocumentTypeId, Is.EqualTo(5));
            Assert.That(filter.Statuses, Is.EqualTo(new[] { DocumentStatus.Accepted }));
            Assert.That(filter.After, Is.Null);
            Assert.That(filter.Take, Is.EqualTo(ExportDocumentsQuery.MaxRows));
            Assert.That(_reads.CountListCalls.Single(), Is.EqualTo(filter));
        });
    }

    [Test]
    public async Task The_file_is_utf8_with_bom_semicolons_crlf_and_colombian_amounts()
    {
        var row = DocumentSamples.Summary(typeId: 5, number: 12, status: DocumentStatus.AcceptedWithWarnings) with
        {
            Totals = new DocumentTotals(1234567.5m, 1234567.5m, 1469135.33m, 0m, 0m, 0m, 1469135.33m),
            EmailStatus = EmailStatus.Failed,
        };
        using var output = new MemoryStream();

        await DocumentCsv.WriteAsync(output, Rows(row), CancellationToken.None);

        var bytes = output.ToArray();
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Multiple(() =>
        {
            Assert.That(bytes.Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.That(text, Is.EqualTo(
                "Tipo;Número;Fecha de emisión;Hora de emisión;Identificación del cliente;Cliente;Subtotal;Impuestos;Total;Estado;Estado del correo;CUFE/CUDE\r\n"
                + "Nota débito;SETP12;2026-01-15;10:30:45;222222222222;Consumidor final;1234567,50;234567,83;1469135,33;Aceptado con advertencias;Falló;cufe12\r\n"));
        });
    }

    [TestCase("Acme SAS", "Acme SAS")]
    [TestCase("Acme; SAS", "\"Acme; SAS\"")]
    [TestCase("Dice \"hola\"", "\"Dice \"\"hola\"\"\"")]
    [TestCase("línea\nnueva", "\"línea\nnueva\"")]
    [TestCase("=1+1", "'=1+1")]
    [TestCase("+57", "'+57")]
    [TestCase("-2", "'-2")]
    [TestCase("@SUM(A1)", "'@SUM(A1)")]
    public void A_customer_name_is_quoted_when_needed_and_never_runs_as_a_formula(string name, string cell)
    {
        var row = DocumentSamples.Summary() with
        {
            Customer = new DocumentCustomer(3, "222222222222", null, name, null, null, null, null, null, null, null, null),
        };

        Assert.That(DocumentCsv.Line(row).Split(';', 6)[5], Does.StartWith(cell));
    }

    [Test]
    public void Every_status_has_a_spanish_label()
    {
        Assert.That(Enum.GetValues<DocumentStatus>().Select(DocumentCsv.StatusLabel), Has.None.Matches<string>(label => Enum.TryParse<DocumentStatus>(label, out _)));
    }

    private static async IAsyncEnumerable<DocumentSummary> Rows(params DocumentSummary[] rows)
    {
        foreach (var row in rows)
        {
            await Task.Yield();
            yield return row;
        }
    }

    private static ExportDocumentsQuery Query(string? kind = null, string? status = null) => new(null, kind, status, null, null, null, null);

    private ExportDocumentsHandler Handler() => new(_reads, new FakeDocumentTenant(_companyId));
}
