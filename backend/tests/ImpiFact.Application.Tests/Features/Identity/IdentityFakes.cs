using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;

namespace ImpiFact.Application.Tests.Features.Identity;

// Dobles de prueba en memoria de los puertos de Identity. Guardan lo que reciben para que las pruebas lo revisen.

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
}

/// <summary>Resumidor transparente: el «resumen» de <c>x</c> es <c>hash:x</c>; los resúmenes «old:» piden rehacerse.</summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    public List<string?> Verified { get; } = [];

    public string Hash(string password) => "hash:" + password;

    public PasswordCheck Verify(string? passwordHash, string password)
    {
        Verified.Add(passwordHash);
        return passwordHash == "hash:" + password ? PasswordCheck.Success
            : passwordHash == "old:" + password ? PasswordCheck.SuccessRehashNeeded
            : PasswordCheck.Failed;
    }
}

public sealed class FakeUserStore : IUserStore
{
    public List<UserAccount> Users { get; } = [];

    public Dictionary<Guid, List<string>> PlatformRoles { get; } = [];

    public List<(Guid UserId, CompanyMembership Membership)> Memberships { get; } = [];

    public List<NewUser> Added { get; } = [];

    public Dictionary<Guid, string> UpdatedHashes { get; } = [];

    public UserAccount AddUser(string email, string passwordHash = "hash:Secreta-123", bool isActive = true, DateTimeOffset? lockoutEnd = null)
    {
        var user = new UserAccount(Guid.CreateVersion7(), email, passwordHash, isActive, lockoutEnd);
        Users.Add(user);
        return user;
    }

    public void Grant(Guid userId, string platformRole) =>
        (PlatformRoles.TryGetValue(userId, out var roles) ? roles : PlatformRoles[userId] = []).Add(platformRole);

    public void Join(Guid userId, Guid companyId, string role) => Memberships.Add((userId, new CompanyMembership(companyId, role)));

    public Task<UserAccount?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        Task.FromResult(Users.FirstOrDefault(user => user.Email == normalizedEmail));

    public Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult(Users.FirstOrDefault(user => user.Id == userId));

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct) =>
        Task.FromResult(Users.Any(user => user.Email == normalizedEmail) || Added.Any(user => user.Email == normalizedEmail));

    public Task<UserRoles> GetRolesAsync(Guid userId, CancellationToken ct) => Task.FromResult(new UserRoles(
        PlatformRoles.TryGetValue(userId, out var roles) ? roles : [],
        [.. Memberships.Where(entry => entry.UserId == userId).Select(entry => entry.Membership)]));

    public Task UpdatePasswordHashAsync(Guid userId, string passwordHash, CancellationToken ct)
    {
        UpdatedHashes[userId] = passwordHash;
        return Task.CompletedTask;
    }

    public Task AddAsync(NewUser user, CancellationToken ct)
    {
        Added.Add(user);
        return Task.CompletedTask;
    }
}

public sealed class FakeRefreshTokenStore : IRefreshTokenStore
{
    public List<RefreshTokenRecord> Tokens { get; } = [];

    public List<(Guid UserId, DateTimeOffset RevokedAt)> RevokedAllFor { get; } = [];

    public Task AddAsync(RefreshTokenRecord token, CancellationToken ct)
    {
        Tokens.Add(token);
        return Task.CompletedTask;
    }

    public Task<RefreshTokenRecord?> FindByHashAsync(string tokenHash, CancellationToken ct) =>
        Task.FromResult(Tokens.FirstOrDefault(token => token.TokenHash == tokenHash));

    public Task<bool> RevokeAsync(Guid tokenId, DateTimeOffset revokedAt, Guid? replacedBy, CancellationToken ct)
    {
        var index = Tokens.FindIndex(token => token.Id == tokenId);
        if (index < 0 || Tokens[index].RevokedAt is not null)
        {
            return Task.FromResult(false);
        }

        Tokens[index] = Tokens[index] with { RevokedAt = revokedAt, ReplacedBy = replacedBy };
        return Task.FromResult(true);
    }

    public Task RevokeAllActiveForUserAsync(Guid userId, DateTimeOffset revokedAt, CancellationToken ct)
    {
        RevokedAllFor.Add((userId, revokedAt));
        return Task.CompletedTask;
    }
}

/// <summary>Tokens de renovación predecibles: <c>refresh-1</c>, <c>refresh-2</c>…; el resumen es <c>sha:valor</c>.</summary>
public sealed class FakeRefreshTokenFactory(FakeClock clock) : IRefreshTokenFactory
{
    private int _count;

    public IssuedRefreshToken Create()
    {
        var value = $"refresh-{++_count}";
        return new IssuedRefreshToken(value, Hash(value), clock.UtcNow.AddDays(14));
    }

    public string Hash(string token) => "sha:" + token;
}

public sealed class FakeAccessTokenIssuer(FakeClock clock) : IAccessTokenIssuer
{
    public List<(Guid UserId, IReadOnlyCollection<string> Roles, Guid? CompanyId)> Issued { get; } = [];

    public AccessToken Issue(Guid userId, IReadOnlyCollection<string> roles, Guid? companyId)
    {
        Issued.Add((userId, roles, companyId));
        return new AccessToken($"jwt-{Issued.Count}", clock.UtcNow.AddMinutes(15));
    }
}

public sealed class FakeApiKeyStore : IApiKeyStore
{
    public List<ApiKeyRecord> Keys { get; } = [];

    public List<NewApiKey> Added { get; } = [];

    public Task<ApiKeyRecord?> FindByHashAsync(string keyHash, CancellationToken ct) =>
        Task.FromResult(Keys.FirstOrDefault(key => key.KeyHash == keyHash));

    public Task MarkUsedAsync(Guid apiKeyId, DateTimeOffset usedAt, CancellationToken ct) => Task.CompletedTask;

    public Task<ApiKeyRecord?> FindByIdAsync(Guid apiKeyId, CancellationToken ct) =>
        Task.FromResult(Keys.FirstOrDefault(key => key.Id == apiKeyId));

    public Task AddAsync(NewApiKey key, CancellationToken ct)
    {
        Added.Add(key);
        return Task.CompletedTask;
    }

    public Task RevokeAsync(Guid apiKeyId, DateTimeOffset revokedAt, CancellationToken ct)
    {
        var index = Keys.FindIndex(key => key.Id == apiKeyId);
        Keys[index] = Keys[index] with { RevokedAt = revokedAt };
        return Task.CompletedTask;
    }
}

public sealed class FakeApiKeyGenerator : IApiKeyGenerator
{
    public GeneratedApiKey Generate() => new("apk_plain-text-key", "apk_plain-te", "hash-of-key");
}

public sealed class FakeCacheInvalidator : IApiKeyCacheInvalidator
{
    public List<string> Invalidated { get; } = [];

    public Task InvalidateAsync(string keyHash, CancellationToken ct)
    {
        Invalidated.Add(keyHash);
        return Task.CompletedTask;
    }
}
