using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Companies;
using ImpiFact.Application.Features.Companies.CreateCompany;
using ImpiFact.Application.Features.Companies.UpdateCompany;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Tests.Features.Companies;

public sealed class UpdateCompanyTests
{
    private FakeCompanyRepository _companies = null!;
    private ImpiFact.Domain.Companies.Company _own = null!;

    [SetUp]
    public void SetUp()
    {
        _companies = new FakeCompanyRepository();
        _own = CompanySamples.Company();
        _companies.Stored.Add(_own);
        _companies.Stored.Add(new CompanyEnvironment { CompanyId = _own.Id, DocumentKind = DocumentKind.Invoice, TypeEnvironmentId = 1 });
    }

    [Test]
    public async Task Company_updates_its_own_data()
    {
        var result = await Handle(Update());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Created, Is.False);
            Assert.That(result.Value.Token, Is.Null);
            Assert.That(result.Value.Message, Is.EqualTo("Empresa creada/actualizada con éxito"));
            Assert.That(_own.Name, Is.EqualTo("EMPRESA DE PRUEBAS"));
            Assert.That(_own.TaxId, Is.EqualTo(15));
            Assert.That(result.Value.Company.Environments.Single().TypeEnvironmentId, Is.EqualTo(1), "Environments are untouched.");
            Assert.That(_companies.AddedOf<CompanyLiability>().Single().TypeLiabilityId, Is.EqualTo(14));
        });
    }

    [Test]
    public async Task Nit_of_another_company_is_forbidden_without_revealing_whether_it_exists()
    {
        var other = CompanySamples.Company(nit: "800111222", email: "otra@example.com");
        _companies.Stored.Add(other);

        var existing = await Handle(Update() with { Nit = other.IdentificationNumber });
        var missing = await Handle(Update() with { Nit = "811000000" });

        Assert.Multiple(() =>
        {
            Assert.That(existing.Error, Is.EqualTo(CompanyErrors.ForeignNit));
            Assert.That(missing.Error, Is.EqualTo(existing.Error), "Same answer whether the NIT exists or not.");
            Assert.That(existing.Error.Type, Is.EqualTo(ErrorType.Forbidden));
            Assert.That(other.Name, Is.EqualTo("EMPRESA ANTERIOR"));
        });
    }

    [Test]
    public async Task Email_of_another_company_is_rejected_with_the_php_message()
    {
        _companies.Stored.Add(CompanySamples.Company(nit: "800111222", email: "otra@example.com"));

        var result = await Handle(Update() with { Email = "otra@example.com" });

        Assert.That(result.Error, Is.EqualTo(CompanyErrors.EmailTaken));
    }

    [Test]
    public void Legacy_create_body_converts_to_an_update_with_the_same_data()
    {
        var create = CompanySamples.Create() with { TypeLiabilityIds = [7], TaxId = 4 };

        var update = new UpdateCompanyCommand(create);

        Assert.Multiple(() =>
        {
            Assert.That(update.Nit, Is.EqualTo(create.Nit));
            Assert.That(update.Dv, Is.EqualTo(create.Dv));
            Assert.That(update.BusinessName, Is.EqualTo(create.BusinessName));
            Assert.That(update.TypeLiabilityIds, Is.EqualTo(create.TypeLiabilityIds));
            Assert.That(update.TaxId, Is.EqualTo(4));
        });
    }

    private UpdateCompanyCommand Update()
    {
        var update = new UpdateCompanyCommand(CompanySamples.Create());
        return update with { Nit = _own.IdentificationNumber };
    }

    private Task<Result<CompanySavedResponse>> Handle(UpdateCompanyCommand command) =>
        new UpdateCompanyHandler(_companies, CompanySamples.CompanyKey(_own.Id)).HandleAsync(command, CancellationToken.None);
}
