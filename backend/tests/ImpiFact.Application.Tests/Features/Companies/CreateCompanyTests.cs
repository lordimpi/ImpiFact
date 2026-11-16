using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Companies;
using ImpiFact.Application.Features.Companies.CreateCompany;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Identity;
using ImpiFact.Domain.Masters;

namespace ImpiFact.Application.Tests.Features.Companies;

public sealed class CreateCompanyTests
{
    private FakeCompanyRepository _companies = null!;
    private FakeApiKeyGenerator _keys = null!;
    private FakeApiKeyStore _keyStore = null!;

    [SetUp]
    public void SetUp()
    {
        _companies = new FakeCompanyRepository();
        _keys = new FakeApiKeyGenerator();
        _keyStore = new FakeApiKeyStore();
    }

    [Test]
    public async Task New_company_takes_the_php_defaults_for_missing_catalogs()
    {
        var result = await Handle(CompanySamples.Create());

        Assert.That(result.IsSuccess, Is.True);
        var company = _companies.AddedOf<Company>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(company.IdentificationNumber, Is.EqualTo(CompanySamples.Nit));
            Assert.That(company.Dv, Is.EqualTo(CompanySamples.Dv));
            Assert.That(company.Name, Is.EqualTo("EMPRESA DE PRUEBAS"));
            Assert.That(company.LanguageId, Is.EqualTo(79));
            Assert.That(company.CountryId, Is.EqualTo(46));
            Assert.That(company.TypeCurrencyId, Is.EqualTo(35));
            Assert.That(company.TypeOperationId, Is.EqualTo(10));
            Assert.That(company.TaxId, Is.EqualTo(15), "Regime 2 defaults the tax to 15.");
            Assert.That(company.Id, Is.Not.EqualTo(Guid.Empty));
        });
    }

    [Test]
    public async Task New_company_has_every_environment_in_habilitation_by_default()
    {
        await Handle(CompanySamples.Create());

        var environments = _companies.AddedOf<CompanyEnvironment>().ToList();
        Assert.That(environments.Select(environment => environment.DocumentKind), Is.EquivalentTo(Enum.GetValues<DocumentKind>()));
        Assert.That(environments.Select(environment => environment.TypeEnvironmentId), Is.All.EqualTo(2));
    }

    [Test]
    public async Task Tax_defaults_to_1_when_the_regime_is_not_2()
    {
        await Handle(CompanySamples.Create() with { TypeRegimeId = 1 });

        Assert.That(_companies.AddedOf<Company>().Single().TaxId, Is.EqualTo(1));
    }

    [Test]
    public async Task Values_in_the_request_win_over_the_defaults()
    {
        await Handle(CompanySamples.Create() with
        {
            TaxId = 4, LanguageId = 80, CountryId = 47, TypeCurrencyId = 36, TypeOperationId = 11, TypeEnvironmentId = 1,
        });

        var company = _companies.AddedOf<Company>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(company.TaxId, Is.EqualTo(4));
            Assert.That(company.LanguageId, Is.EqualTo(80));
            Assert.That(company.CountryId, Is.EqualTo(47));
            Assert.That(company.TypeCurrencyId, Is.EqualTo(36));
            Assert.That(company.TypeOperationId, Is.EqualTo(11));
            Assert.That(
                _companies.AddedOf<CompanyEnvironment>().Single(environment => environment.DocumentKind == DocumentKind.Invoice).TypeEnvironmentId,
                Is.EqualTo(1));
        });
    }

    [Test]
    public async Task New_company_gets_the_php_default_customer()
    {
        await Handle(CompanySamples.Create());

        var company = _companies.AddedOf<Company>().Single();
        var customer = _companies.AddedOf<Customer>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(customer.CompanyId, Is.EqualTo(company.Id));
            Assert.That(customer.IdentificationNumber, Is.EqualTo("2222222222"));
            Assert.That(customer.Name, Is.EqualTo("Cliente por Defecto"));
            Assert.That(customer.Email, Is.EqualTo(company.Email));
        });
    }

    [Test]
    public async Task New_company_gets_an_api_key_shown_once_and_stored_only_as_its_hash()
    {
        var result = await Handle(CompanySamples.Create());

        var key = _keyStore.Added.Single();
        var company = _companies.AddedOf<Company>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Created, Is.True);
            Assert.That(result.Value.Token, Is.EqualTo("apk_plain_1"));
            Assert.That(key.CompanyId, Is.EqualTo(company.Id));
            Assert.That(key.KeyHash, Is.EqualTo(new string('a', 64)));
            Assert.That(key.KeyPrefix, Is.EqualTo("apk_plain_1"));
            Assert.That(key.Scopes, Is.EquivalentTo(new[] { ApiScopes.DocumentsWrite, ApiScopes.DocumentsRead, ApiScopes.ConfigWrite }));
            Assert.That(result.Value.ToString(), Does.Not.Contain("apk_plain_1"));
        });
    }

    [Test]
    public async Task Response_carries_the_php_message_and_the_company_data()
    {
        var result = await Handle(CompanySamples.Create() with { TypeLiabilityIds = [7, 14] });

        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Success, Is.True);
            Assert.That(result.Value.Message, Is.EqualTo("Empresa creada/actualizada con éxito"));
            Assert.That(result.Value.Company.IdentificationNumber, Is.EqualTo(CompanySamples.Nit));
            Assert.That(result.Value.Company.TypeLiabilityIds, Is.EquivalentTo(new[] { 7, 14 }));
            Assert.That(result.Value.Company.Environments, Has.Count.EqualTo(5));
        });
    }

    [Test]
    public async Task Both_liability_fields_are_accepted_and_normalized_to_one_list()
    {
        await Handle(CompanySamples.Create() with { TypeLiabilityId = 14, TypeLiabilityIds = [7, 14, 117] });

        Assert.That(_companies.AddedOf<CompanyLiability>().Select(liability => liability.TypeLiabilityId), Is.EquivalentTo(new[] { 7, 14, 117 }));
    }

    [Test]
    public async Task Same_nit_again_updates_the_company_instead_of_creating_another()
    {
        var existing = CompanySamples.Company();
        _companies.Stored.Add(existing);
        _companies.Stored.Add(new CompanyLiability { CompanyId = existing.Id, TypeLiabilityId = 7 });

        var result = await Handle(CompanySamples.Create() with { TypeLiabilityId = 14 });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Created, Is.False);
            Assert.That(result.Value.Token, Is.Null, "Stored keys cannot be shown again.");
            Assert.That(existing.Name, Is.EqualTo("EMPRESA DE PRUEBAS"));
            Assert.That(existing.LanguageId, Is.EqualTo(79), "Like the PHP, a missing catalog goes back to its default.");
            Assert.That(_companies.Added.OfType<Company>(), Is.Empty);
            Assert.That(_companies.Added.OfType<Customer>(), Is.Empty);
            Assert.That(_keyStore.Added, Is.Empty);
            Assert.That(_companies.Added.OfType<CompanyEnvironment>(), Is.Empty, "Environments only change through SetEnvironment.");
            Assert.That(_companies.Removed.OfType<CompanyLiability>().Single().TypeLiabilityId, Is.EqualTo(7));
            Assert.That(_companies.AddedOf<CompanyLiability>().Single().TypeLiabilityId, Is.EqualTo(14));
        });
    }

    [Test]
    public async Task Update_keeps_the_current_tax_when_none_is_sent_and_the_regime_is_not_2()
    {
        var existing = CompanySamples.Company();
        _companies.Stored.Add(existing);

        await Handle(CompanySamples.Create() with { TypeRegimeId = 1 });

        Assert.That(existing.TaxId, Is.EqualTo(4));
    }

    [Test]
    public async Task Email_of_another_company_is_rejected_with_the_php_message()
    {
        _companies.Stored.Add(CompanySamples.Company(nit: "800111222", email: "TAKEN@example.com"));

        var result = await Handle(CompanySamples.Create(email: "taken@example.com"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Is.EqualTo(CompanyErrors.EmailTaken));
            Assert.That(result.Error.Message, Is.EqualTo(
                "Error de registro, el correo electronico ya existe o es el mismo que tiene registrado actualmente."));
            Assert.That(_companies.Added, Is.Empty);
        });
    }

    [Test]
    public async Task Company_may_keep_its_own_email_when_updating()
    {
        _companies.Stored.Add(CompanySamples.Company(email: "empresa@example.com"));

        var result = await Handle(CompanySamples.Create(email: "empresa@example.com"));

        Assert.That(result.IsSuccess, Is.True);
    }

    [TestCase(ActorType.ApiKey)]
    [TestCase(ActorType.User)]
    [TestCase(ActorType.Anonymous)]
    public async Task Only_the_platform_creates_companies(ActorType actor)
    {
        var result = await Handle(CompanySamples.Create(), new FakeTenant { Actor = actor, CompanyId = Guid.CreateVersion7() });

        Assert.That(result.Error, Is.EqualTo(CompanyErrors.PlatformOnly));
        Assert.That(_companies.Added, Is.Empty);
    }

    private Task<Result<CompanySavedResponse>> Handle(CreateCompanyCommand command, FakeTenant? tenant = null) =>
        new CreateCompanyHandler(_companies, tenant ?? CompanySamples.Platform(), _keys, _keyStore).HandleAsync(command, CancellationToken.None);
}
