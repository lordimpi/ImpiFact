using Dapper;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Domain.Identity;
using ImpiFact.Infrastructure;
using ImpiFact.Infrastructure.Identity;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.IdentityData;

/// <summary>
/// Adaptadores de Identity sobre las tablas reales (Modelo de datos detallado, sección 3.2): API keys por resumen
/// con alcances separados por coma, tokens de renovación con solo su resumen, y usuarios con sus roles de
/// plataforma y de empresa.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class IdentityStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task An_added_api_key_is_found_by_hash_and_by_id_with_its_scopes_and_only_the_hash_is_stored()
    {
        var companyId = await IdentityDatabase.AddCompanyAsync();
        var generated = new ApiKeyGenerator(IdentityDatabase.Services.GetRequiredService<IOptionsService<ApiKeyOptions>>()).Generate();
        var id = Guid.CreateVersion7();

        await using (var scope = IdentityDatabase.Open(companyId))
        {
            await Store<IApiKeyStore>(scope).AddAsync(
                new NewApiKey(id, companyId, "ERP", generated.DisplayPrefix, generated.Hash, [ApiScopes.DocumentsWrite, ApiScopes.DocumentsRead], Now.AddDays(30), null),
                CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        await using var reader = IdentityDatabase.Open(companyId: null);
        var byHash = await Store<IApiKeyStore>(reader).FindByHashAsync(generated.Hash, CancellationToken.None);
        var byId = await Store<IApiKeyStore>(reader).FindByIdAsync(id, CancellationToken.None);
        var row = await QuerySingleAsync<(string KeyHash, string KeyPrefix, string Scopes)>(
            "select key_hash, key_prefix, scopes from api_keys where id = @id", new { id });

        Assert.Multiple(() =>
        {
            Assert.That(byHash, Is.Not.Null);
            Assert.That(byHash!.Id, Is.EqualTo(id));
            Assert.That(byHash.CompanyId, Is.EqualTo(companyId));
            Assert.That(byHash.Scopes, Is.EqualTo(new[] { ApiScopes.DocumentsWrite, ApiScopes.DocumentsRead }));
            Assert.That(byHash.ExpiresAt, Is.EqualTo(Now.AddDays(30)));
            Assert.That(byHash.RevokedAt, Is.Null);
            Assert.That(byId, Is.EqualTo(byHash).Using<ApiKeyRecord>(SameKey));
            Assert.That(row.KeyHash, Is.EqualTo(CredentialHash.Sha256Hex(generated.PlainText)));
            Assert.That(row.Scopes, Is.EqualTo("documents:write,documents:read"));
            Assert.That(row.KeyPrefix, Is.EqualTo(generated.PlainText[..12]));
        });
    }

    [Test]
    public async Task Mark_used_updates_last_used_at_and_never_moves_it_back()
    {
        var id = await AddPlatformKeyAsync();

        await using (var scope = IdentityDatabase.Open(companyId: null))
        {
            await Store<IApiKeyStore>(scope).MarkUsedAsync(id, Now, CancellationToken.None);
        }

        await using (var scope = IdentityDatabase.Open(companyId: null))
        {
            await Store<IApiKeyStore>(scope).MarkUsedAsync(id, Now.AddMinutes(-5), CancellationToken.None);
        }

        var row = await QuerySingleAsync<(DateTime? LastUsedAt, string Scopes, DateTime? RevokedAt)>(
            "select last_used_at, scopes, revoked_at from api_keys where id = @id", new { id });

        Assert.Multiple(() =>
        {
            Assert.That(row.LastUsedAt, Is.EqualTo(Now.UtcDateTime));
            Assert.That(row.Scopes, Is.EqualTo(ApiScopes.Platform));
            Assert.That(row.RevokedAt, Is.Null);
        });
    }

    [Test]
    public async Task Revoke_sets_revoked_at_once()
    {
        var id = await AddPlatformKeyAsync();

        foreach (var revokedAt in new[] { Now, Now.AddHours(1) })
        {
            await using var scope = IdentityDatabase.Open(companyId: null);
            await Store<IApiKeyStore>(scope).RevokeAsync(id, revokedAt, CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        await using var reader = IdentityDatabase.Open(companyId: null);
        var key = await Store<IApiKeyStore>(reader).FindByIdAsync(id, CancellationToken.None);

        Assert.That(key?.RevokedAt, Is.EqualTo(Now));
    }

    [Test]
    public async Task Revoking_all_active_tokens_persists_even_when_the_request_transaction_rolls_back()
    {
        var userId = await AddUserAsync(platformRoles: [], company: null);
        var active = await AddRefreshTokenAsync(userId, revokedAt: null);
        var alreadyRevoked = await AddRefreshTokenAsync(userId, revokedAt: Now.AddDays(-1));

        await using (var scope = IdentityDatabase.Open(companyId: null))
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await using var transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None);
            await Store<IRefreshTokenStore>(scope).RevokeAllActiveForUserAsync(userId, Now, CancellationToken.None);
            await transaction.RollbackAsync(CancellationToken.None);
        }

        await using var reader = IdentityDatabase.Open(companyId: null);
        var store = Store<IRefreshTokenStore>(reader);
        var activeAfter = await store.FindByHashAsync(active.TokenHash, CancellationToken.None);
        var revokedAfter = await store.FindByHashAsync(alreadyRevoked.TokenHash, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(activeAfter?.RevokedAt, Is.EqualTo(Now));
            Assert.That(revokedAfter?.RevokedAt, Is.EqualTo(Now.AddDays(-1)), "An already revoked token keeps its date.");
        });
    }

    [Test]
    public async Task A_rotation_stores_the_replacement_and_only_hashes()
    {
        var userId = await AddUserAsync(platformRoles: [], company: null);
        var old = await AddRefreshTokenAsync(userId, revokedAt: null);
        var replacement = NewToken(userId) with { UserAgent = new string('u', 400) };

        await using (var scope = IdentityDatabase.Open(companyId: null))
        {
            var store = Store<IRefreshTokenStore>(scope);
            await store.AddAsync(replacement, CancellationToken.None);
            await store.RevokeAsync(old.Id, Now, replacement.Id, CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        var row = await QuerySingleAsync<(DateTime? RevokedAt, Guid? ReplacedBy)>(
            "select revoked_at, replaced_by from refresh_tokens where id = @id", new { id = old.Id });
        var newRow = await QuerySingleAsync<(string TokenHash, string UserAgent)>(
            "select token_hash, user_agent from refresh_tokens where id = @id", new { id = replacement.Id });

        Assert.Multiple(() =>
        {
            Assert.That(row.RevokedAt, Is.EqualTo(Now.UtcDateTime));
            Assert.That(row.ReplacedBy, Is.EqualTo(replacement.Id));
            Assert.That(newRow.TokenHash, Is.EqualTo(replacement.TokenHash));
            Assert.That(newRow.UserAgent, Has.Length.EqualTo(300));
        });
    }

    [Test]
    public async Task A_refresh_token_is_found_by_id_even_when_revoked_and_an_unknown_id_finds_nothing()
    {
        var userId = await AddUserAsync(platformRoles: [], company: null);
        var revoked = await AddRefreshTokenAsync(userId, revokedAt: Now.AddMinutes(-1));

        await using var scope = IdentityDatabase.Open(companyId: null);
        var store = Store<IRefreshTokenStore>(scope);
        var found = await store.FindByIdAsync(revoked.Id, CancellationToken.None);
        var missing = await store.FindByIdAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(found?.Id, Is.EqualTo(revoked.Id));
            Assert.That(found?.UserId, Is.EqualTo(userId));
            Assert.That(found?.TokenHash, Is.EqualTo(revoked.TokenHash));
            Assert.That(found?.RevokedAt, Is.EqualTo(Now.AddMinutes(-1)));
            Assert.That(missing, Is.Null);
        });
    }

    [Test]
    public async Task User_roles_include_platform_roles_and_every_company_even_without_a_tenant()
    {
        var first = await IdentityDatabase.AddCompanyAsync();
        var second = await IdentityDatabase.AddCompanyAsync();
        var userId = await AddUserAsync([RoleNames.PlatformAdmin], new CompanyMembership(first, RoleNames.Owner));
        await using (var scope = IdentityDatabase.Open(companyId: null))
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
            db.Add(new UserCompany { UserId = userId, CompanyId = second, RoleId = ImpiFact.Infrastructure.Persistence.Seeding.PlatformSeed.SellerRoleId });
            await db.SaveChangesAsync();
        }

        // Ámbito anónimo, como antes de iniciar sesión: el filtro de empresa no debe ocultar las pertenencias.
        await using var anonymous = IdentityDatabase.Services.CreateAsyncScope();
        var roles = await Store<IUserStore>(anonymous).GetRolesAsync(userId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(roles.PlatformRoles, Is.EqualTo(new[] { RoleNames.PlatformAdmin }));
            Assert.That(roles.RolesIn(first), Is.EqualTo(new[] { RoleNames.Owner }));
            Assert.That(roles.RolesIn(second), Is.EqualTo(new[] { RoleNames.Seller }));
            Assert.That(roles.CompanyIds, Is.EquivalentTo(new[] { first, second }));
        });
    }

    [Test]
    public async Task A_user_is_found_by_normalized_email_with_a_verifiable_password_hash()
    {
        var userId = await AddUserAsync(platformRoles: [], company: null, email: "ana@example.com", password: "Contraseña-Segura-1");

        await using var scope = IdentityDatabase.Services.CreateAsyncScope();
        var account = await Store<IUserStore>(scope).FindByEmailAsync("ana@example.com", CancellationToken.None);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        Assert.Multiple(() =>
        {
            Assert.That(account?.Id, Is.EqualTo(userId));
            Assert.That(account!.IsActive, Is.True);
            Assert.That(account.PasswordHash, Does.Not.Contain("Contraseña-Segura-1"));
            Assert.That(hasher.Verify(account.PasswordHash, "Contraseña-Segura-1"), Is.EqualTo(PasswordCheck.Success));
            Assert.That(hasher.Verify(account.PasswordHash, "otra"), Is.EqualTo(PasswordCheck.Failed));
            Assert.That(hasher.Verify(null, "Contraseña-Segura-1"), Is.EqualTo(PasswordCheck.Failed));
        });
    }

    [Test]
    public void An_unreachable_database_raises_credential_store_unavailable()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = TestDatabase.ProviderName,
                ["Database:ConnectionString"] = UnreachableDatabase.ConnectionString,
            })
            .Build();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddInfrastructure(configuration, TestDatabase.Providers);
        collection.AddIdentityPersistence();
        using var services = collection.BuildServiceProvider();
        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IApiKeyStore>();

        Assert.ThrowsAsync<CredentialStoreUnavailableException>(() => store.FindByHashAsync(new string('a', 64), CancellationToken.None));
    }

    private static bool SameKey(ApiKeyRecord left, ApiKeyRecord right) =>
        left.Id == right.Id && left.CompanyId == right.CompanyId && left.KeyHash == right.KeyHash
        && left.Scopes.SequenceEqual(right.Scopes) && left.ExpiresAt == right.ExpiresAt && left.RevokedAt == right.RevokedAt;

    private static T Store<T>(AsyncServiceScope scope)
        where T : notnull => scope.ServiceProvider.GetRequiredService<T>();

    private static async Task<Guid> AddPlatformKeyAsync()
    {
        var id = Guid.CreateVersion7();
        var hash = CredentialHash.Sha256Hex(Guid.NewGuid().ToString());
        await using var scope = IdentityDatabase.Open(companyId: null);
        await Store<IApiKeyStore>(scope).AddAsync(
            new NewApiKey(id, null, "Plataforma", "apk_platform", hash, [ApiScopes.Platform], null, null), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return id;
    }

    internal static async Task<Guid> AddUserAsync(
        IReadOnlyList<string> platformRoles, CompanyMembership? company, string? email = null, string password = "Contraseña-Segura-1")
    {
        var id = Guid.CreateVersion7();
        await using var scope = IdentityDatabase.Open(companyId: null);
        var hash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(password);
        await Store<IUserStore>(scope).AddAsync(
            new NewUser(id, email ?? $"user-{id:N}@example.com", hash, "Usuario de prueba", null, platformRoles, company), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return id;
    }

    private static RefreshTokenRecord NewToken(Guid userId, DateTimeOffset? revokedAt = null) => new(
        Guid.CreateVersion7(), userId, CredentialHash.Sha256Hex(Guid.NewGuid().ToString()), Now.AddDays(14), revokedAt, null, "127.0.0.1", "tests");

    private static async Task<RefreshTokenRecord> AddRefreshTokenAsync(Guid userId, DateTimeOffset? revokedAt)
    {
        var token = NewToken(userId, revokedAt);
        await using var scope = IdentityDatabase.Open(companyId: null);
        await Store<IRefreshTokenStore>(scope).AddAsync(token, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return token;
    }

    private static async Task<T> QuerySingleAsync<T>(string sql, object parameters)
    {
        await using var connection = TestDatabase.CreateConnection(IdentityDatabase.ConnectionString);
        return await connection.QuerySingleAsync<T>(sql, parameters);
    }
}
