using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Features.Documents.Queries;
using ImpiFact.Application.Features.Documents.Queries.GetDocument;
using ImpiFact.Application.Features.Documents.Queries.ListDocuments;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Tests.Features.Documents;

/// <summary>Consultas canónicas: listado por clave, detalle y búsqueda por CUFE.</summary>
public sealed class DocumentListTests
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
    public void A_cursor_round_trips_the_key()
    {
        var key = new DocumentKey(new DateTimeOffset(2026, 1, 15, 15, 30, 45, 123, TimeSpan.Zero).AddTicks(4560), Guid.CreateVersion7());

        Assert.That(DocumentCursor.TryDecode(DocumentCursor.Encode(key), out var decoded), Is.True);
        Assert.That(decoded, Is.EqualTo(key));
    }

    [TestCase("")]
    [TestCase("not a cursor")]
    [TestCase("MTIz")]
    public void A_malformed_cursor_does_not_decode(string cursor)
    {
        Assert.That(DocumentCursor.TryDecode(cursor, out _), Is.False);
    }

    [Test]
    public async Task First_page_asks_for_one_more_row_and_returns_a_cursor_for_the_next()
    {
        var newest = DocumentSamples.Summary(number: 3, issuedAt: new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero));
        var middle = DocumentSamples.Summary(number: 2, issuedAt: new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var oldest = DocumentSamples.Summary(number: 1, issuedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        _reads.Listed.AddRange([newest, middle, oldest]);

        var result = await Handler().HandleAsync(new ListDocumentsQuery(1, "accepted", "2026-01-01", "2026-01-31", null, 2), CancellationToken.None);

        var filter = _reads.ListCalls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Items.Select(item => item.Number), Is.EqualTo(new long[] { 3, 2 }));
            Assert.That(DocumentCursor.TryDecode(result.Value.NextCursor!, out var next), Is.True);
            Assert.That(next, Is.EqualTo(new DocumentKey(middle.IssuedAt, middle.Id)));
            Assert.That(filter.CompanyId, Is.EqualTo(_companyId));
            Assert.That(filter.Take, Is.EqualTo(3));
            Assert.That(filter.After, Is.Null);
            Assert.That(filter.DocumentTypeId, Is.EqualTo(1));
            Assert.That(filter.Statuses, Is.EqualTo(new[] { DocumentStatus.Accepted }));
            Assert.That(filter.IssuedFrom, Is.EqualTo(new DateTimeOffset(2026, 1, 1, 5, 0, 0, TimeSpan.Zero)));
            Assert.That(filter.IssuedUntil, Is.EqualTo(new DateTimeOffset(2026, 2, 1, 5, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public async Task Last_page_has_no_next_cursor_and_the_cursor_is_passed_to_the_reads()
    {
        var after = new DocumentKey(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), Guid.CreateVersion7());
        _reads.Listed.Add(DocumentSamples.Summary(number: 1));

        var result = await Handler().HandleAsync(new ListDocumentsQuery(null, null, null, null, DocumentCursor.Encode(after), null), CancellationToken.None);

        var filter = _reads.ListCalls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Items, Has.Count.EqualTo(1));
            Assert.That(result.Value.NextCursor, Is.Null);
            Assert.That(filter.After, Is.EqualTo(after));
            Assert.That(filter.Take, Is.EqualTo(ListDocumentsQueryValidator.DefaultLimit + 1));
            Assert.That((filter.DocumentTypeId, filter.Statuses.Count, filter.IssuedFrom, filter.IssuedUntil, filter.Customer, filter.Number), Is.EqualTo(((int?)null, 0, (DateTimeOffset?)null, (DateTimeOffset?)null, (string?)null, (string?)null)));
        });
    }

    [Test]
    public async Task Summary_exposes_the_header_totals_status_and_customer()
    {
        var summary = DocumentSamples.Summary(number: 7, status: DocumentStatus.AcceptedWithWarnings);
        _reads.Listed.Add(summary);

        var item = (await Handler().HandleAsync(new ListDocumentsQuery(null, null, null, null, null, null), CancellationToken.None)).Value.Items.Single();

        Assert.Multiple(() =>
        {
            Assert.That(item.Id, Is.EqualTo(summary.Id));
            Assert.That(item.Status, Is.EqualTo("AcceptedWithWarnings"));
            Assert.That(item.FullNumber, Is.EqualTo("SETP7"));
            Assert.That(item.IssueDate, Is.EqualTo("2026-01-15"));
            Assert.That(item.IssueTime, Is.EqualTo("10:30:45"));
            Assert.That(item.IssuedAt, Is.EqualTo(summary.IssuedAt));
            Assert.That(item.Customer.IdentificationNumber, Is.EqualTo("222222222222"));
            Assert.That(item.Totals.PayableAmount, Is.EqualTo(119m));
            Assert.That(item.Totals.TaxInclusiveAmount, Is.EqualTo(119m));
        });
    }

    [TestCase(null, null, null, null, 0, "limit", "El campo limit debe ser como mínimo 1.")]
    [TestCase(null, null, null, null, 201, "limit", "El campo limit debe ser como máximo 200.")]
    [TestCase("Approved", null, null, null, null, "status", "status es inválido.")]
    [TestCase("4", null, null, null, null, "status", "status es inválido.")]
    [TestCase(null, "2026/01/01", null, null, null, "from", "from no es una fecha válida.")]
    [TestCase(null, null, "ayer", null, null, "to", "to no es una fecha válida.")]
    [TestCase(null, "2026-02-01", "2026-01-01", null, null, "to", "to debe ser una fecha posterior o igual a from.")]
    [TestCase(null, null, null, "zzz", null, "cursor", "cursor es inválido.")]
    public async Task Invalid_filters_are_rejected(string? status, string? from, string? to, string? cursor, int? limit, string field, string message)
    {
        var result = await new ListDocumentsQueryValidator().ValidateAsync(new ListDocumentsQuery(null, status, from, to, cursor, limit));

        Assert.That(result.Errors.Select(error => (error.PropertyName, error.ErrorMessage)), Is.EqualTo(new[] { (field, message) }));
    }

    [Test]
    public async Task Kind_statuses_customer_and_number_reach_the_reads_trimmed_and_without_repeated_statuses()
    {
        var query = new ListDocumentsQuery(null, "accepted, Rejected,ACCEPTED", null, null, null, null, Kind: "credit_note", Customer: "  acme ", Number: " SETP7 ");

        await Handler().HandleAsync(query, CancellationToken.None);

        var filter = _reads.ListCalls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(filter.DocumentTypeId, Is.EqualTo(4));
            Assert.That(filter.Statuses, Is.EqualTo(new[] { DocumentStatus.Accepted, DocumentStatus.Rejected }));
            Assert.That(filter.Customer, Is.EqualTo("acme"));
            Assert.That(filter.Number, Is.EqualTo("SETP7"));
        });
    }

    [Test]
    public async Task Blank_customer_and_number_do_not_filter()
    {
        await Handler().HandleAsync(new ListDocumentsQuery(null, null, null, null, null, null, Customer: "  ", Number: ""), CancellationToken.None);

        var filter = _reads.ListCalls.Single();
        Assert.That((filter.Customer, filter.Number), Is.EqualTo(((string?)null, (string?)null)));
    }

    [Test]
    public async Task Total_count_is_read_only_when_asked_for()
    {
        _reads.Listed.Add(DocumentSamples.Summary(number: 1));
        _reads.Total = 42;

        var without = await Handler().HandleAsync(new ListDocumentsQuery(null, null, null, null, null, null), CancellationToken.None);
        var with = await Handler().HandleAsync(new ListDocumentsQuery(null, null, null, null, null, null, IncludeTotal: true), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(without.Value.TotalCount, Is.Null);
            Assert.That(with.Value.TotalCount, Is.EqualTo(42));
            Assert.That(_reads.CountListCalls, Has.Count.EqualTo(1));
        });
    }

    [TestCase(1, "invoice")]
    [TestCase(4, "credit_note")]
    [TestCase(5, "debit_note")]
    [TestCase(11, "support_document")]
    [TestCase(13, "support_document_adjustment_note")]
    [TestCase(9, null)]
    public async Task Every_row_says_its_kind_and_email_status(int typeId, string? kind)
    {
        _reads.Listed.Add(DocumentSamples.Summary(typeId: typeId) with { EmailStatus = EmailStatus.Failed });

        var item = (await Handler().HandleAsync(new ListDocumentsQuery(null, null, null, null, null, null), CancellationToken.None)).Value.Items.Single();

        Assert.That((item.Kind, item.EmailStatus), Is.EqualTo((kind, "Failed")));
    }

    [TestCase(null, "receipt", null, null, "kind", "kind es inválido.")]
    [TestCase(4, "invoice", null, null, "kind", "kind no coincide con document_type_id.")]
    [TestCase(null, null, "accepted,", null, "status", "status es inválido.")]
    [TestCase(null, null, "", null, "status", "status es inválido.")]
    [TestCase(null, null, null, "SETP-1", "number", "number es inválido.")]
    [TestCase(null, null, null, "1234567890123456789012345678901", "number", "number es inválido.")]
    public async Task Invalid_panel_filters_are_rejected(int? typeId, string? kind, string? status, string? number, string field, string message)
    {
        var result = await new ListDocumentsQueryValidator().ValidateAsync(
            new ListDocumentsQuery(typeId, status, null, null, null, null, Kind: kind, Number: number));

        Assert.That(result.Errors.Select(error => (error.PropertyName, error.ErrorMessage)), Is.EqualTo(new[] { (field, message) }));
    }

    [Test]
    public async Task A_matching_kind_and_document_type_id_are_accepted()
    {
        var result = await new ListDocumentsQueryValidator().ValidateAsync(new ListDocumentsQuery(4, null, null, null, null, null, Kind: "CREDIT_NOTE"));

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public async Task A_customer_text_longer_than_100_characters_is_rejected()
    {
        var result = await new ListDocumentsQueryValidator().ValidateAsync(
            new ListDocumentsQuery(null, null, null, null, null, null, Customer: new string('a', 101)));

        Assert.That(result.Errors.Select(error => (error.PropertyName, error.ErrorMessage)),
            Is.EqualTo(new[] { ("customer", "customer no debe ser mayor que 100 caracteres.") }));
    }

    [Test]
    public async Task Detail_of_a_document_that_is_not_in_the_company_is_not_found()
    {
        var result = await new GetDocumentHandler(_reads, new FakeDocumentTenant(_companyId))
            .HandleAsync(new GetDocumentQuery(Guid.CreateVersion7()), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(DocumentErrors.NotFound));
    }

    [Test]
    public async Task Detail_by_cufe_returns_the_lines_and_taxes()
    {
        var header = DocumentSamples.Summary(number: 9);
        var iva = new DocumentTaxAmount(1, 19m, 100m, 19m, null, null);
        _reads.Details.Add(new DocumentDetail(
            header, "CUFE-SHA384", null, null, 10, null, null, null, "nota", null, null, null, null, null, null, null,
            [iva],
            [new DocumentLineDetail(1, "A1", "Producto", null, 1, 70, 1m, 1m, 100m, 100m, false, null, [iva])]));

        var result = await new GetDocumentByCufeHandler(_reads, new FakeDocumentTenant(_companyId))
            .HandleAsync(new GetDocumentByCufeQuery("cufe9"), CancellationToken.None);

        var detail = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(detail.Id, Is.EqualTo(header.Id));
            Assert.That(detail.UuidAlgorithm, Is.EqualTo("CUFE-SHA384"));
            Assert.That(detail.Notes, Is.EqualTo("nota"));
            Assert.That(detail.Taxes.Single().TaxAmount, Is.EqualTo(19m));
            Assert.That(detail.Lines.Single().Description, Is.EqualTo("Producto"));
            Assert.That(detail.Lines.Single().Taxes.Single().Percent, Is.EqualTo(19m));
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task An_empty_cufe_is_rejected(string cufe)
    {
        var result = await new GetDocumentByCufeQueryValidator().ValidateAsync(new GetDocumentByCufeQuery(cufe));

        Assert.That(result.Errors.Select(error => (error.PropertyName, error.ErrorMessage)), Is.EqualTo(new[] { ("cufe", "El campo cufe es obligatorio.") }));
    }

    [Test]
    public async Task A_cufe_longer_than_the_column_is_rejected()
    {
        var result = await new GetDocumentByCufeQueryValidator().ValidateAsync(new GetDocumentByCufeQuery(new string('a', 97)));

        Assert.That(result.Errors.Select(error => (error.PropertyName, error.ErrorMessage)),
            Is.EqualTo(new[] { ("cufe", "cufe no debe ser mayor que 96 caracteres.") }));
    }

    private ListDocumentsHandler Handler() => new(_reads, new FakeDocumentTenant(_companyId));
}
