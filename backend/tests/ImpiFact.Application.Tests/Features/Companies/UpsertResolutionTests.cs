using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Companies;
using ImpiFact.Application.Features.Companies.UpsertResolution;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Tests.Features.Companies;

public sealed class UpsertResolutionTests
{
    private FakeCompanyRepository _companies = null!;
    private Company _own = null!;
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 16, 3, 0, 0, TimeSpan.Zero));

    [SetUp]
    public void SetUp()
    {
        _companies = new FakeCompanyRepository();
        _own = CompanySamples.Company();
        _companies.Stored.Add(_own);
        _companies.Stored.Add(new CompanyEnvironment { CompanyId = _own.Id, DocumentKind = DocumentKind.Invoice, TypeEnvironmentId = 2 });
        _companies.Stored.Add(new CompanyEnvironment { CompanyId = _own.Id, DocumentKind = DocumentKind.Payroll, TypeEnvironmentId = 1 });
    }

    [Test]
    public async Task New_invoice_resolution_is_stored_in_the_environment_of_its_family()
    {
        var result = await Handle(Invoice());

        var row = _companies.AddedOf<Resolution>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Message, Is.EqualTo("Resolución creada/actualizada con éxito"));
            Assert.That(row.CompanyId, Is.EqualTo(_own.Id));
            Assert.That(row.DocumentTypeId, Is.EqualTo(1));
            Assert.That(row.TypeEnvironmentId, Is.EqualTo(2));
            Assert.That(row.Prefix, Is.EqualTo("SETP"));
            Assert.That(row.ResolutionNumber, Is.EqualTo("18760000001"));
            Assert.That(row.ResolutionDate, Is.EqualTo(new DateOnly(2019, 1, 19)));
            Assert.That(row.TechnicalKey, Is.EqualTo("fc8eac422eba16e22ffd8c6f94b3f40a6e38162c"));
            Assert.That((row.RangeFrom, row.RangeTo), Is.EqualTo((990000000L, 995000000L)));
            Assert.That((row.ValidFrom, row.ValidTo), Is.EqualTo(((DateOnly?)new DateOnly(2019, 1, 19), (DateOnly?)new DateOnly(2030, 1, 19))));
            Assert.That(row.IsActive, Is.True);
            Assert.That(result.Value.Resolution.Resolution, Is.EqualTo("18760000001"));
        });
    }

    [Test]
    public async Task Same_type_number_prefix_and_environment_updates_the_existing_resolution()
    {
        var existing = new Resolution
        {
            CompanyId = _own.Id, DocumentTypeId = 1, TypeEnvironmentId = 2, Prefix = "SETP", ResolutionNumber = "18760000001",
            RangeFrom = 1, RangeTo = 10, IsActive = false,
        };
        _companies.Stored.Add(existing);

        await Handle(Invoice());

        Assert.Multiple(() =>
        {
            Assert.That(_companies.AddedOf<Resolution>(), Is.Empty);
            Assert.That((existing.RangeFrom, existing.RangeTo), Is.EqualTo((990000000L, 995000000L)));
            Assert.That(existing.TechnicalKey, Is.EqualTo("fc8eac422eba16e22ffd8c6f94b3f40a6e38162c"));
            Assert.That(existing.IsActive, Is.True);
        });
    }

    [Test]
    public async Task Delete_all_type_resolutions_deactivates_the_others_of_the_same_type_and_environment()
    {
        var other = new Resolution
        {
            CompanyId = _own.Id, DocumentTypeId = 1, TypeEnvironmentId = 2, Prefix = "FE", ResolutionNumber = "1", RangeFrom = 1, RangeTo = 2,
            IsActive = true,
        };
        _companies.Stored.Add(other);

        await Handle(Invoice() with { DeleteAllTypeResolutions = true });

        Assert.That(other.IsActive, Is.False, "Old resolutions are kept inactive: issued documents may reference them.");
    }

    [Test]
    public async Task Credit_note_resolution_needs_only_its_range()
    {
        var result = await Handle(new UpsertResolutionCommand { TypeDocumentId = 4, Prefix = "NC", From = 1, To = 99999999 });

        var row = _companies.AddedOf<Resolution>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(row.ResolutionNumber, Is.EqualTo(""));
            Assert.That(row.ResolutionDate, Is.EqualTo(new DateOnly(2026, 11, 15)), "Without a date, today in Colombia (model gap).");
            Assert.That(row.TypeEnvironmentId, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Payroll_resolution_goes_to_the_payroll_environment()
    {
        await Handle(new UpsertResolutionCommand { TypeDocumentId = 9, Prefix = "NI", From = 1, To = 1000 });

        Assert.That(_companies.AddedOf<Resolution>().Single().TypeEnvironmentId, Is.EqualTo(1));
    }

    [Test]
    public async Task Nit_of_another_company_is_forbidden()
    {
        var result = await Handle(Invoice() with { Nit = "800111222" });

        Assert.That(result.Error, Is.EqualTo(CompanyErrors.ForeignNit));
    }

    [Test]
    public async Task Prefix_has_at_most_4_characters()
    {
        var result = await Validate(Invoice() with { Prefix = "SETPX" });

        Assert.That(Messages(result)["prefix"], Is.EqualTo("prefix no debe ser mayor que 4 caracteres."));
    }

    [Test]
    public async Task Invoice_resolution_requires_number_date_key_and_validity()
    {
        var result = await Validate(new UpsertResolutionCommand { TypeDocumentId = 1, From = 1, To = 2 });

        var errors = Messages(result);
        Assert.Multiple(() =>
        {
            Assert.That(errors["resolution"], Is.EqualTo("El campo resolution es obligatorio cuando type document id es 1."));
            Assert.That(errors["technical_key"], Is.EqualTo("El campo technical key es obligatorio cuando type document id es 1."));
            Assert.That(errors["resolution_date"], Is.EqualTo("El campo resolution date es obligatorio cuando type document id es 1."));
            Assert.That(errors["date_from"], Is.EqualTo("El campo date from es obligatorio cuando type document id es 1."));
            Assert.That(errors["date_to"], Is.EqualTo("El campo date to es obligatorio cuando type document id es 1."));
        });
    }

    [Test]
    public async Task Range_end_must_be_greater_than_its_start()
    {
        var result = await Validate(Invoice() with { From = 100, To = 100 });

        Assert.That(Messages(result)["to"], Is.EqualTo("El campo to debe ser como mínimo 101."));
    }

    [Test]
    public async Task Validity_must_end_after_it_starts_and_dates_use_the_php_format()
    {
        var result = await Validate(Invoice() with { DateFrom = "2030-01-19", DateTo = "2019-01-19", ResolutionDate = "19/01/2019" });

        var errors = Messages(result);
        Assert.That(errors["date_to"], Is.EqualTo("date to debe ser una fecha posterior a date from."));
        Assert.That(errors["resolution_date"], Is.EqualTo("resolution date no corresponde al formato Y-m-d."));
    }

    [Test]
    public async Task Unknown_document_type_is_rejected()
    {
        var result = await Validate(Invoice() with { TypeDocumentId = 999 }, new FakeCatalogLookup().Missing<ImpiFact.Domain.Catalog.TypeDocument>(999));

        Assert.That(Messages(result)["type_document_id"], Is.EqualTo("type document id es inválido."));
    }

    private static UpsertResolutionCommand Invoice() => new()
    {
        TypeDocumentId = 1,
        Prefix = "SETP",
        Resolution = "18760000001",
        ResolutionDate = "2019-01-19",
        TechnicalKey = "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c",
        From = 990000000,
        To = 995000000,
        DateFrom = "2019-01-19",
        DateTo = "2030-01-19",
    };

    private static Task<FluentValidation.Results.ValidationResult> Validate(UpsertResolutionCommand command, FakeCatalogLookup? catalogs = null) =>
        new UpsertResolutionValidator(catalogs ?? new FakeCatalogLookup()).ValidateAsync(command);

    private static Dictionary<string, string> Messages(FluentValidation.Results.ValidationResult result) =>
        result.Errors.GroupBy(error => error.PropertyName).ToDictionary(group => group.Key, group => group.First().ErrorMessage);

    private Task<Result<ResolutionSavedResponse>> Handle(UpsertResolutionCommand command) =>
        new UpsertResolutionHandler(_companies, CompanySamples.CompanyKey(_own.Id), _clock).HandleAsync(command, CancellationToken.None);
}
