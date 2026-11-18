using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Identity;
using ImpiFact.Application.Features.Identity.ApiKeys;
using ImpiFact.Application.Features.Identity.Users;
using ImpiFact.Application.Tests.Behaviors;

namespace ImpiFact.Application.Tests.Features.Identity;

/// <summary>
/// Crear y revocar API keys y crear usuarios: quién puede hacerlo (empresa u owner de la empresa, o la plataforma),
/// qué alcances caben en cada llave, que el texto de la llave salga una sola vez y que revocar limpie la caché.
/// </summary>
[TestFixture]
public sealed class CredentialHandlerTests
{
    private FakeClock _clock = null!;
    private FakeUserStore _users = null!;
    private FakeApiKeyStore _keys = null!;
    private FakeCacheInvalidator _cache = null!;

    [SetUp]
    public void CreateFakes()
    {
        _clock = new FakeClock();
        _users = new FakeUserStore();
        _keys = new FakeApiKeyStore();
        _cache = new FakeCacheInvalidator();
    }

    [Test]
    public async Task A_company_credential_creates_a_company_key_and_gets_the_plain_text_once()
    {
        var companyId = Guid.CreateVersion7();
        var tenant = new FakeTenant { CompanyId = companyId, Actor = ActorType.ApiKey, Scopes = CompanyScopes };

        var result = await CreateKey(tenant, [ApiScopes.DocumentsWrite, ApiScopes.DocumentsWrite, ApiScopes.DocumentsRead]);

        Assert.That(result.IsSuccess, Is.True);
        var stored = _keys.Added.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Key, Is.EqualTo("apk_plain-text-key"));
            Assert.That(result.Value.KeyPrefix, Is.EqualTo("apk_plain-te"));
            Assert.That(result.Value.CompanyId, Is.EqualTo(companyId));
            Assert.That(result.Value.Scopes, Is.EqualTo(new[] { ApiScopes.DocumentsWrite, ApiScopes.DocumentsRead }));
            Assert.That(result.Value.ToString(), Does.Not.Contain("apk_plain-text-key"));
            Assert.That(stored.Id, Is.EqualTo(result.Value.Id));
            Assert.That(stored.KeyHash, Is.EqualTo("hash-of-key"));
            Assert.That(stored.CompanyId, Is.EqualTo(companyId));
            Assert.That(stored.ToString(), Does.Not.Contain("apk_plain-text-key"));
        });
    }

    // Una credencial solo entrega alcances que ella misma tiene: una llave con config:write no fabrica documents:write.
    [Test]
    public async Task A_company_credential_cannot_mint_scopes_it_does_not_hold()
    {
        var tenant = new FakeTenant
        {
            CompanyId = Guid.CreateVersion7(), Actor = ActorType.ApiKey, Scopes = new HashSet<string> { ApiScopes.ConfigWrite },
        };

        var result = await CreateKey(tenant, [ApiScopes.DocumentsWrite]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(IdentityErrors.ApiKeyScopeNotAllowed));
            Assert.That(_keys.Added, Is.Empty);
        });
    }

    [Test]
    public async Task A_company_key_never_gets_the_platform_scope()
    {
        var tenant = new FakeTenant { CompanyId = Guid.CreateVersion7(), Actor = ActorType.ApiKey };

        var result = await CreateKey(tenant, [ApiScopes.DocumentsRead, ApiScopes.Platform]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(IdentityErrors.ApiKeyScopeNotAllowed));
            Assert.That(_keys.Added, Is.Empty);
        });
    }

    [Test]
    public async Task A_panel_user_creates_company_keys_only_as_owner_of_that_company()
    {
        var companyId = Guid.CreateVersion7();
        var owner = _users.AddUser("owner@example.com");
        _users.Join(owner.Id, companyId, "owner");
        var seller = _users.AddUser("seller@example.com");
        _users.Join(seller.Id, companyId, "seller");

        var byOwner = await CreateKey(new FakeTenant { CompanyId = companyId, UserId = owner.Id, Actor = ActorType.User, Scopes = CompanyScopes }, [ApiScopes.DocumentsWrite]);
        var bySeller = await CreateKey(new FakeTenant { CompanyId = companyId, UserId = seller.Id, Actor = ActorType.User, Scopes = CompanyScopes }, [ApiScopes.DocumentsWrite]);

        Assert.Multiple(() =>
        {
            Assert.That(byOwner.IsSuccess, Is.True);
            Assert.That(_keys.Added.Single().CreatedBy, Is.EqualTo(owner.Id));
            Assert.That(bySeller.Error, Is.EqualTo(IdentityErrors.Forbidden));
        });
    }

    [Test]
    public async Task The_platform_creates_platform_keys_with_the_platform_scope_only()
    {
        var platform = new FakeTenant { Actor = ActorType.Platform };

        var created = await CreateKey(platform, [ApiScopes.Platform]);
        var mixed = await CreateKey(platform, [ApiScopes.Platform, ApiScopes.DocumentsWrite]);

        Assert.Multiple(() =>
        {
            Assert.That(created.IsSuccess, Is.True);
            Assert.That(created.Value.CompanyId, Is.Null);
            Assert.That(mixed.Error, Is.EqualTo(IdentityErrors.ApiKeyScopeNotAllowed));
        });
    }

    [Test]
    public async Task Without_company_only_a_platform_actor_creates_keys()
    {
        var admin = _users.AddUser("admin@example.com");
        _users.Grant(admin.Id, "platform_admin");
        var plain = _users.AddUser("plain@example.com");

        var byAdmin = await CreateKey(new FakeTenant { UserId = admin.Id, Actor = ActorType.User }, [ApiScopes.Platform]);
        var byPlainUser = await CreateKey(new FakeTenant { UserId = plain.Id, Actor = ActorType.User }, [ApiScopes.Platform]);
        var byAnonymous = await CreateKey(new FakeTenant { Actor = ActorType.Anonymous }, [ApiScopes.Platform]);

        Assert.Multiple(() =>
        {
            Assert.That(byAdmin.IsSuccess, Is.True);
            Assert.That(byPlainUser.Error, Is.EqualTo(IdentityErrors.Forbidden));
            Assert.That(byAnonymous.Error, Is.EqualTo(IdentityErrors.Forbidden));
        });
    }

    [Test]
    public async Task Revoking_a_key_sets_revoked_at_and_clears_the_cache_entry()
    {
        var companyId = Guid.CreateVersion7();
        var key = AddKey(companyId);

        var result = await RevokeKey(new FakeTenant { CompanyId = companyId, Actor = ActorType.ApiKey }, key.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Is.EqualTo(new RevokedApiKey(key.Id, _clock.UtcNow)));
            Assert.That(_keys.Keys.Single().RevokedAt, Is.EqualTo(_clock.UtcNow));
            Assert.That(_cache.Invalidated, Is.EqualTo(new[] { key.KeyHash }));
        });
    }

    [Test]
    public async Task A_key_of_another_company_is_reported_as_not_found()
    {
        var key = AddKey(Guid.CreateVersion7());

        var result = await RevokeKey(new FakeTenant { CompanyId = Guid.CreateVersion7(), Actor = ActorType.ApiKey }, key.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(IdentityErrors.ApiKeyNotFound));
            Assert.That(_keys.Keys.Single().RevokedAt, Is.Null);
            Assert.That(_cache.Invalidated, Is.Empty);
        });
    }

    [Test]
    public async Task The_platform_revokes_any_key_and_revoking_twice_keeps_the_first_date()
    {
        var key = AddKey(Guid.CreateVersion7());
        var platform = new FakeTenant { Actor = ActorType.Platform };

        var first = await RevokeKey(platform, key.Id);
        var revokedAt = _clock.UtcNow;
        _clock.UtcNow = _clock.UtcNow.AddHours(1);
        var second = await RevokeKey(platform, key.Id);

        Assert.Multiple(() =>
        {
            Assert.That(first.Value.RevokedAt, Is.EqualTo(revokedAt));
            Assert.That(second.Value.RevokedAt, Is.EqualTo(revokedAt));
            Assert.That(_cache.Invalidated, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Without_company_a_non_platform_actor_cannot_revoke_and_learns_nothing()
    {
        var key = AddKey(Guid.CreateVersion7());

        var result = await RevokeKey(new FakeTenant { Actor = ActorType.Anonymous }, key.Id);

        Assert.That(result.Error, Is.EqualTo(IdentityErrors.Forbidden));
    }

    [Test]
    public async Task An_owner_creates_a_seller_of_the_company_with_a_hashed_password_and_normalized_email()
    {
        var companyId = Guid.CreateVersion7();
        var owner = _users.AddUser("owner@example.com");
        _users.Join(owner.Id, companyId, "owner");

        var result = await CreateUser(
            new FakeTenant { CompanyId = companyId, UserId = owner.Id, Actor = ActorType.User }, " Nuevo@Example.com ", "seller");

        Assert.That(result.IsSuccess, Is.True);
        var added = _users.Added.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Is.EqualTo(new CreatedUser(added.Id, "nuevo@example.com", "Usuario Nuevo", "seller", companyId)));
            Assert.That(added.PasswordHash, Is.EqualTo("hash:Contraseña-Larga-1"));
            Assert.That(added.Company, Is.EqualTo(new CompanyMembership(companyId, "seller")));
            Assert.That(added.PlatformRoles, Is.Empty);
            Assert.That(added.ToString(), Does.Not.Contain("Contraseña"));
        });
    }

    [Test]
    public async Task Only_an_owner_creates_company_users_and_only_with_company_roles()
    {
        var companyId = Guid.CreateVersion7();
        var owner = _users.AddUser("owner@example.com");
        _users.Join(owner.Id, companyId, "owner");
        var seller = _users.AddUser("seller@example.com");
        _users.Join(seller.Id, companyId, "seller");

        var bySeller = await CreateUser(new FakeTenant { CompanyId = companyId, UserId = seller.Id, Actor = ActorType.User }, "a@example.com", "seller");
        var byCompanyKey = await CreateUser(new FakeTenant { CompanyId = companyId, Actor = ActorType.ApiKey }, "b@example.com", "seller");
        var adminByOwner = await CreateUser(new FakeTenant { CompanyId = companyId, UserId = owner.Id, Actor = ActorType.User }, "c@example.com", "platform_admin");

        Assert.Multiple(() =>
        {
            Assert.That(bySeller.Error, Is.EqualTo(IdentityErrors.Forbidden));
            Assert.That(byCompanyKey.Error, Is.EqualTo(IdentityErrors.Forbidden));
            Assert.That(adminByOwner.Error, Is.EqualTo(IdentityErrors.RoleNotAllowed));
            Assert.That(_users.Added, Is.Empty);
        });
    }

    [Test]
    public async Task The_platform_creates_platform_admins_without_company()
    {
        var created = await CreateUser(new FakeTenant { Actor = ActorType.Platform }, "admin@example.com", "platform_admin");
        var owner = await CreateUser(new FakeTenant { Actor = ActorType.Platform }, "owner@example.com", "owner");

        Assert.Multiple(() =>
        {
            Assert.That(created.Value.CompanyId, Is.Null);
            Assert.That(_users.Added.Single().PlatformRoles, Is.EqualTo(new[] { "platform_admin" }));
            Assert.That(_users.Added.Single().Company, Is.Null);
            Assert.That(owner.Error, Is.EqualTo(IdentityErrors.RoleNotAllowed));
        });
    }

    [Test]
    public async Task A_taken_email_is_a_conflict()
    {
        _users.AddUser("ana@example.com");

        var result = await CreateUser(new FakeTenant { Actor = ActorType.Platform }, "ANA@example.com", "platform_admin");

        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(IdentityErrors.EmailTaken));
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Conflict));
        });
    }

    private ApiKeyRecord AddKey(Guid? companyId)
    {
        var key = new ApiKeyRecord(Guid.CreateVersion7(), companyId, "hash-" + Guid.NewGuid(), [ApiScopes.DocumentsRead], null, null);
        _keys.Keys.Add(key);
        return key;
    }

    private static readonly HashSet<string> CompanyScopes = [ApiScopes.DocumentsWrite, ApiScopes.DocumentsRead, ApiScopes.ConfigWrite];

    private Task<Result<CreatedApiKey>> CreateKey(ITenantContext tenant, string[] scopes) =>
        new CreateApiKeyHandler(tenant, _users, new FakeApiKeyGenerator(), _keys)
            .HandleAsync(new CreateApiKeyCommand(" ERP ", scopes, null), CancellationToken.None);

    private Task<Result<RevokedApiKey>> RevokeKey(ITenantContext tenant, Guid id) =>
        new RevokeApiKeyHandler(tenant, _users, _keys, _cache, _clock).HandleAsync(new RevokeApiKeyCommand(id), CancellationToken.None);

    [Test]
    public async Task The_platform_creates_the_first_owner_or_seller_of_an_existing_company()
    {
        var companyId = Guid.CreateVersion7();
        _users.Companies.Add(companyId);
        var platform = new FakeTenant { Actor = ActorType.Platform };

        var owner = await CreateUser(platform, "duena@example.com", "owner", companyId);
        var seller = await CreateUser(platform, "vende@example.com", "seller", companyId);

        Assert.Multiple(() =>
        {
            Assert.That(owner.Value.CompanyId, Is.EqualTo(companyId));
            Assert.That(owner.Value.Role, Is.EqualTo("owner"));
            Assert.That(seller.Value.Role, Is.EqualTo("seller"));
            Assert.That(_users.Added.Select(user => user.Company), Is.EqualTo(new[] { new CompanyMembership(companyId, "owner"), new CompanyMembership(companyId, "seller") }));
            Assert.That(_users.Added.All(user => user.PlatformRoles.Count == 0), Is.True);
        });
    }

    [Test]
    public async Task A_company_for_another_tenant_or_an_unknown_company_is_refused()
    {
        var companyId = Guid.CreateVersion7();
        var otherId = Guid.CreateVersion7();
        _users.Companies.Add(companyId);
        _users.Companies.Add(otherId);
        var owner = _users.AddUser("owner@example.com");
        _users.Memberships.Add((owner.Id, new CompanyMembership(companyId, "owner")));
        var ownerSession = new FakeTenant { CompanyId = companyId, UserId = owner.Id, Actor = ActorType.User };
        var platform = new FakeTenant { Actor = ActorType.Platform };

        var crossTenant = await CreateUser(ownerSession, "x@example.com", "seller", otherId);
        var unknown = await CreateUser(platform, "y@example.com", "owner", Guid.CreateVersion7());
        var adminInCompany = await CreateUser(platform, "z@example.com", "platform_admin", companyId);
        var customer = await CreateUser(platform, "w@example.com", "customer", companyId);

        Assert.Multiple(() =>
        {
            Assert.That(crossTenant.Error, Is.EqualTo(IdentityErrors.Forbidden));
            Assert.That(unknown.Error, Is.EqualTo(IdentityErrors.CompanyNotFound));
            Assert.That(adminInCompany.Error, Is.EqualTo(IdentityErrors.RoleNotAllowed));
            Assert.That(customer.Error, Is.EqualTo(IdentityErrors.RoleNotAllowed));
            Assert.That(_users.Added, Is.Empty);
        });
    }

    private Task<Result<CreatedUser>> CreateUser(ITenantContext tenant, string email, string role, Guid? companyId = null) =>
        new CreateUserHandler(tenant, _users, new FakePasswordHasher())
            .HandleAsync(new CreateUserCommand(email, "Contraseña-Larga-1", " Usuario Nuevo ", null, role, companyId), CancellationToken.None);
}
