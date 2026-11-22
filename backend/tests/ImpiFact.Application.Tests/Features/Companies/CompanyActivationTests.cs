using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Companies.Activation;
using ImpiFact.Application.Tests.Features.Identity;
using ImpiFact.Domain.Companies;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImpiFact.Application.Tests.Features.Companies;

/// <summary>Deactivating and reactivating a company (phase 7a, T9b1): platform only, idempotent, revokes keys and sessions.</summary>
public sealed class CompanyActivationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 15, 0, 0, TimeSpan.Zero);

    private FakeCompanyRepository _companies = null!;
    private FakeApiKeyStore _keys = null!;
    private FakeCacheInvalidator _cache = null!;
    private FakeMembers _members = null!;
    private FakeRefreshTokenStore _refresh = null!;
    private Company _company = null!;

    [SetUp]
    public void SetUp()
    {
        _companies = new FakeCompanyRepository();
        _company = CompanySamples.Company();
        _companies.Stored.Add(_company);
        _keys = new FakeApiKeyStore();
        _keys.ActiveByCompany[_company.Id] = ["hash-1", "hash-2"];
        _cache = new FakeCacheInvalidator();
        _members = new FakeMembers();
        _refresh = new FakeRefreshTokenStore();
    }

    public static IEnumerable<TestCaseData> NotPlatform()
    {
        yield return new TestCaseData(new FakeTenant { CompanyId = Guid.NewGuid(), UserId = Guid.NewGuid(), Actor = ActorType.User, Scopes = Scopes(ApiScopes.ConfigWrite) })
            .SetArgDisplayNames("owner");
        yield return new TestCaseData(new FakeTenant { CompanyId = Guid.NewGuid(), Actor = ActorType.ApiKey, Scopes = Scopes(ApiScopes.ConfigWrite) })
            .SetArgDisplayNames("company key");
        yield return new TestCaseData(new FakeTenant { UserId = Guid.NewGuid(), Actor = ActorType.User })
            .SetArgDisplayNames("user without platform scope");
    }

    [TestCaseSource(nameof(NotPlatform))]
    public async Task Only_the_platform_deactivates_or_reactivates(FakeTenant tenant)
    {
        var deactivate = await Deactivate(tenant);
        _company.IsActive = false;
        var reactivate = await Reactivate(tenant);

        Assert.Multiple(() =>
        {
            Assert.That(deactivate.Error.Code, Is.EqualTo("company.platform_only"));
            Assert.That(deactivate.Error.Message, Is.EqualTo("Solo el administrador de la plataforma puede desactivar o reactivar empresas."));
            Assert.That(reactivate.Error.Code, Is.EqualTo("company.platform_only"));
            Assert.That(_company.IsActive, Is.False, "Reactivation was refused.");
            Assert.That(_keys.RevokedCompanies, Is.Empty);
            Assert.That(_refresh.RevokedWithChangesFor, Is.Empty);
        });
    }

    [Test]
    public async Task Deactivating_revokes_the_keys_and_the_sessions_of_its_users_except_the_acting_one()
    {
        var admin = Platform();
        var owner = Guid.NewGuid();
        var seller = Guid.NewGuid();
        _members.Users[_company.Id] = [owner, seller, admin.UserId!.Value];

        var result = await Deactivate(admin);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.IsActive, Is.False);
            Assert.That(result.Value.Changed, Is.True);
            Assert.That(result.Value.RevokedApiKeys, Is.EqualTo(2));
            Assert.That(result.Value.SignedOutUsers, Is.EqualTo(2));
            Assert.That(_company.IsActive, Is.False);
            Assert.That(_keys.RevokedCompanies, Is.EqualTo(new[] { (_company.Id, Now) }));
            Assert.That(_cache.Invalidated, Is.EqualTo(new[] { "hash-1", "hash-2" }));
            Assert.That(_refresh.RevokedWithChangesFor, Is.EqualTo(new[] { (owner, Now), (seller, Now) }));
        });
    }

    [Test]
    public async Task Deactivating_an_inactive_company_or_reactivating_an_active_one_does_nothing()
    {
        _members.Users[_company.Id] = [Guid.NewGuid()];
        var reactivated = await Reactivate(Platform());
        _company.IsActive = false;

        var deactivated = await Deactivate(Platform());

        Assert.Multiple(() =>
        {
            Assert.That(reactivated.Value.Changed, Is.False);
            Assert.That(deactivated.Value.Changed, Is.False);
            Assert.That(deactivated.Value.IsActive, Is.False);
            Assert.That(deactivated.Value.RevokedApiKeys, Is.Zero);
            Assert.That(_keys.RevokedCompanies, Is.Empty);
            Assert.That(_cache.Invalidated, Is.Empty);
            Assert.That(_refresh.RevokedWithChangesFor, Is.Empty);
        });
    }

    [Test]
    public async Task Reactivating_only_sets_the_company_active()
    {
        _company.IsActive = false;

        var result = await Reactivate(Platform());

        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Changed, Is.True);
            Assert.That(result.Value.IsActive, Is.True);
            Assert.That(_company.IsActive, Is.True);
            Assert.That(_keys.RevokedCompanies, Is.Empty, "Keys are neither revoked nor brought back.");
        });
    }

    [Test]
    public async Task An_unknown_nit_is_not_found()
    {
        var deactivate = await new DeactivateCompanyHandler(
            Platform(), _companies, _keys, _cache, _members, _refresh, new FakeClock(Now), NullLogger<DeactivateCompanyHandler>.Instance)
            .HandleAsync(new DeactivateCompanyCommand("123"), CancellationToken.None);
        var reactivate = await new ReactivateCompanyHandler(Platform(), _companies, NullLogger<ReactivateCompanyHandler>.Instance)
            .HandleAsync(new ReactivateCompanyCommand("123"), CancellationToken.None);

        Assert.That((deactivate.Error.Code, reactivate.Error.Code), Is.EqualTo(("company.not_found", "company.not_found")));
    }

    private static HashSet<string> Scopes(params string[] scopes) => [.. scopes];

    private static FakeTenant Platform() => new() { UserId = Guid.NewGuid(), Actor = ActorType.User, Scopes = Scopes(ApiScopes.Platform) };

    private Task<Abstractions.Results.Result<CompanyActivationResponse>> Deactivate(FakeTenant tenant) =>
        new DeactivateCompanyHandler(tenant, _companies, _keys, _cache, _members, _refresh, new FakeClock(Now), NullLogger<DeactivateCompanyHandler>.Instance)
            .HandleAsync(new DeactivateCompanyCommand(_company.IdentificationNumber), CancellationToken.None);

    private Task<Abstractions.Results.Result<CompanyActivationResponse>> Reactivate(FakeTenant tenant) =>
        new ReactivateCompanyHandler(tenant, _companies, NullLogger<ReactivateCompanyHandler>.Instance)
            .HandleAsync(new ReactivateCompanyCommand(_company.IdentificationNumber), CancellationToken.None);

    /// <summary>Active users of each company, as <see cref="ICompanyMemberStore.ListActiveUserIdsAsync"/> returns them.</summary>
    private sealed class FakeMembers : ICompanyMemberStore
    {
        public Dictionary<Guid, List<Guid>> Users { get; } = [];

        public Task<IReadOnlyList<Guid>> ListActiveUserIdsAsync(Guid companyId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Guid>>(Users.GetValueOrDefault(companyId, []));

        public Task<Guid?> FindCompanyIdAsync(string nit, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<CompanyMember>> ListAsync(Guid companyId, CancellationToken ct) => throw new NotSupportedException();

        public Task<CompanyMemberChange> ChangeAsync(Guid companyId, Guid userId, string role, bool isActive, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
