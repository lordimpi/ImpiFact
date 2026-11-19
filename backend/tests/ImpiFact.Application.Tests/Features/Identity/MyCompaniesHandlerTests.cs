using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Identity.Me;
using ImpiFact.Application.Tests.Behaviors;

namespace ImpiFact.Application.Tests.Features.Identity;

/// <summary>
/// Empresas de quien llama según su credencial: las API keys no tienen roles ni ven empresas ajenas, y la plataforma no
/// entra en ninguna. Los usuarios del panel se prueban contra PostgreSQL (<c>MyCompaniesTests</c>).
/// </summary>
[TestFixture]
public sealed class MyCompaniesHandlerTests
{
    [Test]
    public async Task A_platform_api_key_has_no_companies_and_a_company_key_only_its_own()
    {
        var companyId = Guid.CreateVersion7();
        var reads = new FakeCompanyReads();
        reads.Companies[companyId] = new AccessibleCompany(companyId, "Empresa", "900123456", "8", true, ["owner"]);

        var platform = await new ListMyCompaniesHandler(new FakeTenant { Actor = ActorType.Platform }, reads)
            .HandleAsync(new ListMyCompaniesQuery(), CancellationToken.None);
        var companyKey = await new ListMyCompaniesHandler(new FakeTenant { Actor = ActorType.ApiKey, CompanyId = companyId }, reads)
            .HandleAsync(new ListMyCompaniesQuery(), CancellationToken.None);
        var foreignKey = await new ListMyCompaniesHandler(new FakeTenant { Actor = ActorType.ApiKey, CompanyId = Guid.CreateVersion7() }, reads)
            .HandleAsync(new ListMyCompaniesQuery(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(platform.Value.Items, Is.Empty);
            Assert.That(companyKey.Value.Items.Select(company => (company.Id, company.Name, company.Nit, company.Dv, company.IsActive)),
                Is.EqualTo(new[] { (companyId, "Empresa", "900123456", "8", true) }));
            Assert.That(companyKey.Value.Items.Single().Roles, Is.Empty, "An API key has no role.");
            Assert.That(foreignKey.Value.Items, Is.Empty);
        });
    }

    private sealed class FakeCompanyReads : IUserCompanyReads
    {
        public Dictionary<Guid, AccessibleCompany> Companies { get; } = [];

        public Task<IReadOnlyList<AccessibleCompany>> ForUserAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AccessibleCompany>>([]);

        public Task<AccessibleCompany?> ByIdAsync(Guid companyId, CancellationToken ct) =>
            Task.FromResult(Companies.GetValueOrDefault(companyId));
    }
}
