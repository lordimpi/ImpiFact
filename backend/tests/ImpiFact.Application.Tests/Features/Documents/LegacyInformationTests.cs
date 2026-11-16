using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Features.Documents.Queries;
using ImpiFact.Application.Features.Documents.Queries.Information;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Tests.Features.Documents;

/// <summary>Consultas heredadas <c>information</c> y <c>total_documents</c> (ResumeController.php del PHP).</summary>
public sealed class LegacyInformationTests
{
    private const string OwnNit = "900123456";
    private static readonly DateTimeOffset NineteenHundred = new(1900, 1, 1, 5, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AfterTwentyOneHundred = new(2100, 1, 2, 5, 0, 0, TimeSpan.Zero);

    private FakeDocumentReads _reads = null!;
    private Guid _companyId;

    [SetUp]
    public void SetUp()
    {
        _companyId = Guid.CreateVersion7();
        _reads = new FakeDocumentReads();
        _reads.Companies.Add(new DocumentCompany(_companyId, OwnNit, "EMPRESA DE PRUEBAS SAS"));
    }

    [TestCase(null, null, "1900-01-01", "2100-01-01")]
    [TestCase("2026-01-15", null, "2026-01-15", "2026-01-15")]
    [TestCase(null, "2026-02-01", "2026-02-01", "2026-02-01")]
    [TestCase("", "", "1900-01-01", "2100-01-01")]
    [TestCase("2026-01-01", "2026-01-31", "2026-01-01", "2026-01-31")]
    public void Date_defaults_follow_the_php(string? desde, string? hasta, string expectedFrom, string expectedTo)
    {
        var (from, to) = LegacyDateRange.Resolve(desde, hasta);

        Assert.That((from, to), Is.EqualTo((DateOnly.Parse(expectedFrom), DateOnly.Parse(expectedTo))));
    }

    [Test]
    public void A_colombian_day_starts_and_ends_at_five_utc()
    {
        var range = ColombiaDates.Range(new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 15));

        Assert.That(range, Is.EqualTo(new IssuedRange(
            new DateTimeOffset(2026, 1, 15, 5, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 16, 5, 0, 0, TimeSpan.Zero))));
    }

    [TestCase(DocumentStatus.Received, 0)]
    [TestCase(DocumentStatus.Queued, 0)]
    [TestCase(DocumentStatus.Sent, 0)]
    [TestCase(DocumentStatus.Accepted, 1)]
    [TestCase(DocumentStatus.AcceptedWithWarnings, 1)]
    [TestCase(DocumentStatus.Rejected, 0)]
    [TestCase(DocumentStatus.Unconfirmed, 0)]
    [TestCase(DocumentStatus.Failed, 0)]
    [TestCase(DocumentStatus.ContingencyPending, 2)]
    public void Legacy_state_maps_from_the_status(DocumentStatus status, int expected)
    {
        Assert.That(LegacyDocumentState.Of(status), Is.EqualTo(expected));
    }

    [Test]
    public async Task Information_without_dates_reads_every_validated_document_of_the_four_php_types()
    {
        _reads.ByType[1] = [DocumentSamples.Summary(typeId: 1, number: 990000001), DocumentSamples.Summary(typeId: 1, number: 990000002)];
        _reads.ByType[4] = [DocumentSamples.Summary(typeId: 4, number: 5)];

        var result = await new GetInformationHandler(_reads, new FakeDocumentTenant(_companyId))
            .HandleAsync(new GetInformationQuery(OwnNit, null, null), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var response = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.True);
            Assert.That(response.Message, Is.EqualTo("NIT Encontrado"));
            Assert.That(response.Company, Is.EqualTo("EMPRESA DE PRUEBAS SAS"));
            Assert.That(response.Data.Select(group => group.TypeDocumentId), Is.EqualTo(new int?[] { 1, 4, 5, 15 }));
            Assert.That(response.Data.Select(group => group.Name),
                Is.EqualTo(new[] { "Factura de Venta Nacional", "Nota Credito", "Nota Debito", "Documento Equivalente POS" }));
            Assert.That(response.Data.Select(group => group.Count), Is.EqualTo(new[] { 2, 1, 0, 0 }));
            Assert.That(response.Data[0].Documents.Select(document => document.Number), Is.EqualTo(new long[] { 990000001, 990000002 }));
            Assert.That(_reads.ListByTypeCalls.Select(call => call.TypeId), Is.EqualTo(new[] { 1, 4, 5, 15 }));
            Assert.That(_reads.ListByTypeCalls.Select(call => call.Range), Is.All.EqualTo((IssuedRange?)new IssuedRange(NineteenHundred, AfterTwentyOneHundred)));
            Assert.That(_reads.ListByTypeCalls.Select(call => call.Page), Is.All.Null);
            Assert.That(_reads.ListByTypeCalls[0].Statuses,
                Is.EquivalentTo(new[] { DocumentStatus.Accepted, DocumentStatus.AcceptedWithWarnings }));
        });
    }

    [Test]
    public async Task Information_documents_keep_the_php_fields()
    {
        var summary = DocumentSamples.Summary(typeId: 1, number: 990000001, issuedAt: new DateTimeOffset(2026, 1, 15, 15, 30, 45, TimeSpan.Zero));
        _reads.ByType[1] = [summary];

        var result = await new GetInformationHandler(_reads, new FakeDocumentTenant(_companyId))
            .HandleAsync(new GetInformationQuery(OwnNit, "2026-01-15", null), CancellationToken.None);

        var document = result.Value.Data[0].Documents.Single();
        Assert.Multiple(() =>
        {
            Assert.That(document.Id, Is.EqualTo(summary.Id));
            Assert.That(document.IdentificationNumber, Is.EqualTo(OwnNit));
            Assert.That(document.StateDocumentId, Is.EqualTo(1));
            Assert.That(document.TypeDocumentId, Is.EqualTo(1));
            Assert.That((document.Prefix, document.Number, document.Cufe), Is.EqualTo(("SETP", 990000001L, "cufe990000001")));
            Assert.That(document.Customer, Is.EqualTo("222222222222"));
            Assert.That(document.Client.Name, Is.EqualTo("Consumidor final"));
            Assert.That(document.Client.Email, Is.EqualTo("cliente@example.com"));
            Assert.That(document.CurrencyId, Is.EqualTo(35));
            Assert.That(document.DateIssue, Is.EqualTo("2026-01-15 10:30:45"), "Colombian local time, as the PHP stored it.");
            Assert.That((document.Sale, document.TotalDiscount, document.TotalTax, document.Subtotal, document.Total),
                Is.EqualTo((119m, 0m, 19m, 100m, 119m)));
            Assert.That(document.AmbientId, Is.EqualTo(2));
            Assert.That(_reads.ListByTypeCalls[0].Range, Is.EqualTo((IssuedRange?)new IssuedRange(
                new DateTimeOffset(2026, 1, 15, 5, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 16, 5, 0, 0, TimeSpan.Zero))));
        });
    }

    [TestCase("800111222")]
    [TestCase("900123456-7")]
    public async Task Information_for_a_nit_that_is_not_the_credentials_is_forbidden_without_reading_documents(string nit)
    {
        var result = await new GetInformationHandler(_reads, new FakeDocumentTenant(_companyId))
            .HandleAsync(new GetInformationQuery(nit, null, null), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(DocumentErrors.ForeignNit));
        Assert.That(_reads.ListByTypeCalls, Is.Empty);
    }

    [Test]
    public async Task A_credential_whose_company_is_gone_gets_the_same_forbidden_answer()
    {
        var result = await new GetInformationHandler(_reads, new FakeDocumentTenant(Guid.CreateVersion7()))
            .HandleAsync(new GetInformationQuery(OwnNit, null, null), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(DocumentErrors.ForeignNit));
    }

    [TestCase("1", 0)]
    [TestCase("0", 0)]
    [TestCase("-4", 0)]
    [TestCase("3", 200)]
    public async Task Information_by_page_reads_one_hundred_per_type_and_page(string page, int skip)
    {
        _reads.ByType[15] = [DocumentSamples.Summary(typeId: 15)];

        var result = await new GetInformationPageHandler(_reads, new FakeDocumentTenant(_companyId))
            .HandleAsync(new GetInformationPageQuery(OwnNit, page), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_reads.ListByTypeCalls.Select(call => call.Page), Is.All.EqualTo(new PageWindow(skip, 100)));
            Assert.That(_reads.ListByTypeCalls.Select(call => call.Range), Is.All.Null, "The PHP page variant has no date filter.");
            Assert.That(result.Value.Data.Select(group => group.TypeDocumentId), Is.All.Null, "The PHP page variant has no type_document_id.");
            Assert.That(result.Value.Data.Select(group => group.Count), Is.EqualTo(new[] { 0, 0, 0, 1 }));
            Assert.That(result.Value.Message, Is.EqualTo("NIT Encontrado"));
        });
    }

    [Test]
    public async Task Total_documents_counts_the_eight_php_types_in_order()
    {
        _reads.Counts[1] = 7;
        _reads.Counts[26] = 2;

        var result = await new GetTotalDocumentsHandler(_reads, new FakeDocumentTenant(_companyId))
            .HandleAsync(new GetTotalDocumentsQuery(OwnNit, "2026-01-01", "2026-01-31"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Message, Is.EqualTo("NIT Encontrado"));
            Assert.That(result.Value.Company, Is.EqualTo("EMPRESA DE PRUEBAS SAS"));
            Assert.That(result.Value.Data.Select(total => total.Name), Is.EqualTo(new[]
            {
                "Factura de Venta Nacional", "Nota Credito", "Nota Debito", "Documento Soporte a No Obligados a Facturar",
                "Notas de Ajuste al Documento Soporte a No Obligados a Facturar", "Documento Equivalente POS",
                "Nota Credito a Documento POS", "Nota Debito a Documento POS",
            }));
            Assert.That(result.Value.Data.Select(total => total.Count), Is.EqualTo(new long[] { 7, 0, 0, 0, 0, 0, 2, 0 }));
            Assert.That(_reads.CountCalls.Single().TypeIds, Is.EqualTo(new[] { 1, 4, 5, 11, 13, 15, 26, 25 }));
            Assert.That(_reads.CountCalls.Single().Range, Is.EqualTo(new IssuedRange(
                new DateTimeOffset(2026, 1, 1, 5, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 1, 5, 0, 0, TimeSpan.Zero))));
        });
    }

    [Test]
    public async Task Total_documents_for_another_nit_is_forbidden()
    {
        var result = await new GetTotalDocumentsHandler(_reads, new FakeDocumentTenant(_companyId))
            .HandleAsync(new GetTotalDocumentsQuery("800111222", null, null), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(DocumentErrors.ForeignNit));
        Assert.That(_reads.CountCalls, Is.Empty);
    }

    [TestCase("15-01-2026", null, "desde", "desde no es una fecha válida.")]
    [TestCase("2026-02-30", null, "desde", "desde no es una fecha válida.")]
    [TestCase("2026-01-01", "mañana", "hasta", "hasta no es una fecha válida.")]
    public async Task Invalid_dates_are_rejected_with_the_laravel_message(string? desde, string? hasta, string field, string message)
    {
        var information = await new GetInformationQueryValidator().ValidateAsync(new GetInformationQuery(OwnNit, desde, hasta));
        var totals = await new GetTotalDocumentsQueryValidator().ValidateAsync(new GetTotalDocumentsQuery(OwnNit, desde, hasta));

        Assert.Multiple(() =>
        {
            Assert.That(information.Errors.Select(error => (error.PropertyName, error.ErrorMessage)), Is.EqualTo(new[] { (field, message) }));
            Assert.That(totals.Errors.Select(error => (error.PropertyName, error.ErrorMessage)), Is.EqualTo(new[] { (field, message) }));
        });
    }

    [Test]
    public async Task Valid_and_missing_dates_pass_validation()
    {
        var result = await new GetInformationQueryValidator().ValidateAsync(new GetInformationQuery(OwnNit, "2026-01-01", null));

        Assert.That(result.IsValid, Is.True);
    }

    [TestCase("abc")]
    [TestCase("1.5")]
    [TestCase("99999999999")]
    public async Task A_page_that_is_not_an_integer_is_rejected(string page)
    {
        var result = await new GetInformationPageQueryValidator().ValidateAsync(new GetInformationPageQuery(OwnNit, page));

        Assert.That(result.Errors.Select(error => (error.PropertyName, error.ErrorMessage)),
            Is.EqualTo(new[] { ("page", "page debe ser un número entero.") }));
    }
}
