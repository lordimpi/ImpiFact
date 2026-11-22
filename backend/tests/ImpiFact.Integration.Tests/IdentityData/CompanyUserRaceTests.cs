using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Identity;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Seeding;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.IdentityData;

/// <summary>
/// Simultaneous changes to the users of a company (<c>PATCH /api/v1/companies/{nit}/users/{userId}</c>) and the
/// isolation of an owner between companies. A <see cref="MemberStoreGate"/> pauses one request inside
/// <see cref="ICompanyMemberStore.ChangeAsync"/> so that another one can commit in between: the company never ends
/// without an active owner, and a change that fails leaves the sessions of its target untouched.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class CompanyUserRaceTests
{
    private const string Password = "Contraseña-Segura-1";

    private readonly MemberStoreGate _gate = new();
    private IdentityDataApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi() => _factory = new IdentityDataApiFactory(services =>
    {
        var descriptor = services.Last(service => service.ServiceType == typeof(ICompanyMemberStore));
        services.Remove(descriptor);
        services.AddScoped(descriptor.ImplementationType!);
        services.AddScoped<ICompanyMemberStore>(provider =>
            new GatedMemberStore((ICompanyMemberStore)provider.GetRequiredService(descriptor.ImplementationType!), _gate));
    });

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    [TearDown]
    public void ReleaseGate() => _gate.Open();

    [Test]
    public async Task Two_owners_demoting_each_other_one_after_the_check_of_the_other_keep_an_active_owner()
    {
        var company = await AddCompanyAsync();
        var first = await AddUserAsync(company.Id, RoleNames.Owner);
        var second = await AddUserAsync(company.Id, RoleNames.Owner);
        using var byFirst = await ClientAsync(first.Email, company.Id);
        using var bySecond = await ClientAsync(second.Email, company.Id);
        var path = $"/api/v1/companies/{company.Nit}/users";

        // First demotes second and pauses inside ChangeAsync, after the use case already counted two active owners.
        _gate.Arm(second.Id, MemberStoreGate.Mode.Before, expected: 1);
        var firstTask = byFirst.PatchAsJsonAsync($"{path}/{second.Id}", new { role = "seller" });
        await _gate.WaitReachedAsync();
        // Meanwhile second demotes first and commits.
        using var secondResponse = await bySecond.PatchAsJsonAsync($"{path}/{first.Id}", new { role = "seller" });
        _gate.Open();
        using var firstResponse = await firstTask;

        Assert.Multiple(async () =>
        {
            Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await firstResponse.Content.ReadAsStringAsync());
            Assert.That(await CodeAndDetailAsync(firstResponse), Is.EqualTo("user.last_owner|La empresa debe conservar al menos un dueño activo."));
            Assert.That(await ActiveOwnersAsync(company.Id), Is.EqualTo(1), "The company was left without any active owner.");
            Assert.That(await RoleCountAsync(second.Id, company.Id, PlatformSeed.OwnerRoleId), Is.EqualTo(1), "The refused change left second as owner.");
        });
    }

    [Test]
    public async Task Two_owners_deactivating_each_other_at_once_conflict_and_keep_an_active_owner()
    {
        var company = await AddCompanyAsync();
        var first = await AddUserAsync(company.Id, RoleNames.Owner);
        var second = await AddUserAsync(company.Id, RoleNames.Owner);
        using var byFirst = await ClientAsync(first.Email, company.Id);
        using var bySecond = await ClientAsync(second.Email, company.Id);
        var path = $"/api/v1/companies/{company.Nit}/users";

        // Both pause after ChangeAsync loaded and changed their rows, before saving.
        _gate.Arm(null, MemberStoreGate.Mode.After, expected: 2);
        var firstTask = byFirst.PatchAsJsonAsync($"{path}/{second.Id}", new { is_active = false });
        var secondTask = bySecond.PatchAsJsonAsync($"{path}/{first.Id}", new { is_active = false });
        await _gate.WaitReachedAsync();
        _gate.Open();
        using var firstResponse = await firstTask;
        using var secondResponse = await secondTask;

        Assert.Multiple(async () =>
        {
            Assert.That(await ActiveOwnersAsync(company.Id), Is.EqualTo(1), "The company was left without any active owner.");
            Assert.That(new[] { firstResponse.StatusCode, secondResponse.StatusCode },
                Is.EquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        });
    }

    [Test]
    public async Task A_deactivation_that_conflicts_leaves_the_refresh_token_of_its_target_usable()
    {
        var company = await AddCompanyAsync();
        var first = await AddUserAsync(company.Id, RoleNames.Owner);
        var second = await AddUserAsync(company.Id, RoleNames.Owner);
        var firstSession = await LoginAsync(first.Email, company.Id);
        var secondSession = await LoginAsync(second.Email, company.Id);
        using var byFirst = _factory.CreateClient(firstSession.AccessToken);
        using var bySecond = _factory.CreateClient(secondSession.AccessToken);
        var path = $"/api/v1/companies/{company.Nit}/users";

        _gate.Arm(null, MemberStoreGate.Mode.After, expected: 2);
        var firstTask = byFirst.PatchAsJsonAsync($"{path}/{second.Id}", new { is_active = false });
        var secondTask = bySecond.PatchAsJsonAsync($"{path}/{first.Id}", new { is_active = false });
        await _gate.WaitReachedAsync();
        _gate.Open();
        using var firstResponse = await firstTask;
        using var secondResponse = await secondTask;

        // The request that lost the conflict targeted the actor of the request that won, who stays an active owner.
        var firstLost = firstResponse.StatusCode == HttpStatusCode.Conflict;
        var keeper = firstLost ? (Id: second.Id, Session: secondSession) : (Id: first.Id, Session: firstSession);
        var removed = firstLost ? first.Id : second.Id;
        using var refresh = await RefreshAsync(keeper.Session.RefreshToken, company.Id);

        Assert.Multiple(async () =>
        {
            Assert.That(new[] { firstResponse.StatusCode, secondResponse.StatusCode },
                Is.EquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
            Assert.That(await ActiveTokensAsync(keeper.Id), Is.EqualTo(1), "The failed deactivation revoked no token.");
            Assert.That(refresh.StatusCode, Is.EqualTo(HttpStatusCode.OK), await refresh.Content.ReadAsStringAsync());
            Assert.That(await ActiveTokensAsync(removed), Is.Zero, "The deactivation that succeeded revoked its target's tokens.");
        });
    }

    [Test]
    public async Task A_deactivation_refused_as_last_owner_inside_the_change_leaves_the_refresh_token_usable()
    {
        var company = await AddCompanyAsync();
        var first = await AddUserAsync(company.Id, RoleNames.Owner);
        var second = await AddUserAsync(company.Id, RoleNames.Owner);
        using var byFirst = await ClientAsync(first.Email, company.Id);
        using var bySecond = await ClientAsync(second.Email, company.Id);
        var secondSession = await LoginAsync(second.Email, company.Id);
        var path = $"/api/v1/companies/{company.Nit}/users";

        // First deactivates second and pauses after the use case counted two active owners; second demotes first.
        _gate.Arm(second.Id, MemberStoreGate.Mode.Before, expected: 1);
        var firstTask = byFirst.PatchAsJsonAsync($"{path}/{second.Id}", new { is_active = false });
        await _gate.WaitReachedAsync();
        using var demoted = await bySecond.PatchAsJsonAsync($"{path}/{first.Id}", new { role = "seller" });
        _gate.Open();
        using var refused = await firstTask;
        using var refresh = await RefreshAsync(secondSession.RefreshToken, company.Id);

        Assert.Multiple(async () =>
        {
            Assert.That(demoted.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await refused.Content.ReadAsStringAsync());
            Assert.That(await CodeAsync(refused), Is.EqualTo("user.last_owner"));
            Assert.That(await IsActiveAsync(second.Id, company.Id), Is.True);
            Assert.That(refresh.StatusCode, Is.EqualTo(HttpStatusCode.OK), "The refused deactivation closed no session.");
        });
    }

    [Test]
    public async Task An_owner_of_two_companies_logged_into_one_cannot_touch_the_other()
    {
        var a = await AddCompanyAsync();
        var b = await AddCompanyAsync();
        var both = await AddUserAsync(a.Id, RoleNames.Owner);
        await JoinAsync(both.Id, b.Id, RoleNames.Owner);
        var bSeller = await AddUserAsync(b.Id, RoleNames.Seller);
        var missing = MissingNit();
        using var inA = await ClientAsync(both.Email, a.Id);

        using var listB = await inA.GetAsync($"/api/v1/companies/{b.Nit}/users");
        using var listMissing = await inA.GetAsync($"/api/v1/companies/{missing}/users");
        using var patchB = await inA.PatchAsJsonAsync($"/api/v1/companies/{b.Nit}/users/{bSeller.Id}", new { role = "owner" });
        using var patchBInA = await inA.PatchAsJsonAsync($"/api/v1/companies/{a.Nit}/users/{bSeller.Id}", new { role = "owner" });
        using var patchRandomInA = await inA.PatchAsJsonAsync($"/api/v1/companies/{a.Nit}/users/{Guid.CreateVersion7()}", new { role = "owner" });

        Assert.Multiple(async () =>
        {
            Assert.That(listB.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(await CodeAndDetailAsync(listB), Is.EqualTo(await CodeAndDetailAsync(listMissing)));
            Assert.That(patchB.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(patchBInA.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(await CodeAndDetailAsync(patchBInA), Is.EqualTo(await CodeAndDetailAsync(patchRandomInA)));
            Assert.That(await RoleCountAsync(bSeller.Id, b.Id, PlatformSeed.OwnerRoleId), Is.Zero);
        });
    }

    [Test]
    public async Task A_deactivated_owner_with_a_live_access_token_cannot_change_users_or_create_keys()
    {
        var company = await AddCompanyAsync();
        var owner = await AddUserAsync(company.Id, RoleNames.Owner);
        var partner = await AddUserAsync(company.Id, RoleNames.Owner);
        var seller = await AddUserAsync(company.Id, RoleNames.Seller);
        using var byOwner = await ClientAsync(owner.Email, company.Id);
        using var byPartner = await ClientAsync(partner.Email, company.Id);
        var path = $"/api/v1/companies/{company.Nit}/users";

        using var deactivated = await byOwner.PatchAsJsonAsync($"{path}/{partner.Id}", new { is_active = false });
        using var reactivateSelf = await byPartner.PatchAsJsonAsync($"{path}/{partner.Id}", new { is_active = true });
        using var promote = await byPartner.PatchAsJsonAsync($"{path}/{seller.Id}", new { role = "owner" });
        using var deactivateOther = await byPartner.PatchAsJsonAsync($"{path}/{owner.Id}", new { is_active = false });
        using var apiKey = await byPartner.PostAsJsonAsync("/api/v1/api-keys", new { name = "x", scopes = new[] { "config:write" } });

        Assert.Multiple(async () =>
        {
            Assert.That(deactivated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(reactivateSelf.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(promote.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(deactivateOther.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(apiKey.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(await IsActiveAsync(partner.Id, company.Id), Is.False);
            Assert.That(await IsActiveAsync(owner.Id, company.Id), Is.True);
            Assert.That(await RoleCountAsync(seller.Id, company.Id, PlatformSeed.OwnerRoleId), Is.Zero);
        });
    }

    private static string MissingNit() => Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture) + "9";

    private static async Task<(Guid Id, string Nit)> AddCompanyAsync()
    {
        var id = await IdentityDatabase.AddCompanyAsync();
        await using var connection = TestDatabase.Connect(IdentityDatabase.ConnectionString);
        return (id, await connection.QuerySingleAsync<string>("select identification_number from companies where id = @id", new { id }));
    }

    private static async Task<(Guid Id, string Email)> AddUserAsync(Guid companyId, string role)
    {
        var email = $"race-{Guid.NewGuid():N}@example.com";
        var id = await IdentityStoreTests.AddUserAsync([], new CompanyMembership(companyId, role), email, Password);
        return (id, email);
    }

    private static async Task JoinAsync(Guid userId, Guid companyId, string role)
    {
        await using var scope = IdentityDatabase.Open(companyId);
        var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
        db.Add(new UserCompany { UserId = userId, CompanyId = companyId, RoleId = PlatformSeed.Roles.Single(candidate => candidate.Name == role).Id });
        await db.SaveChangesAsync();
    }

    private async Task<(string AccessToken, string RefreshToken)> LoginAsync(string email, Guid companyId)
    {
        using var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password, company_id = companyId });
        var tokens = await ReadAsync(response);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), tokens.GetRawText());
        return (tokens.GetProperty("access_token").GetString()!, tokens.GetProperty("refresh_token").GetString()!);
    }

    private async Task<HttpClient> ClientAsync(string email, Guid companyId) =>
        _factory.CreateClient((await LoginAsync(email, companyId)).AccessToken);

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken, Guid companyId) =>
        _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refresh_token = refreshToken, company_id = companyId });

    private static async Task<long> ActiveOwnersAsync(Guid companyId)
    {
        await using var connection = TestDatabase.Connect(IdentityDatabase.ConnectionString);
        return await connection.QuerySingleAsync<long>(
            "select count(*) from user_companies where company_id = @companyId and role_id = @owner and is_active = @active",
            new { companyId, owner = PlatformSeed.OwnerRoleId, active = true });
    }

    private static async Task<long> ActiveTokensAsync(Guid userId)
    {
        await using var connection = TestDatabase.Connect(IdentityDatabase.ConnectionString);
        return await connection.QuerySingleAsync<long>("select count(*) from refresh_tokens where user_id = @userId and revoked_at is null", new { userId });
    }

    private static async Task<long> RoleCountAsync(Guid userId, Guid companyId, Guid roleId)
    {
        await using var connection = TestDatabase.Connect(IdentityDatabase.ConnectionString);
        return await connection.QuerySingleAsync<long>(
            "select count(*) from user_companies where user_id = @userId and company_id = @companyId and role_id = @roleId", new { userId, companyId, roleId });
    }

    private static async Task<bool> IsActiveAsync(Guid userId, Guid companyId)
    {
        await using var connection = TestDatabase.Connect(IdentityDatabase.ConnectionString);
        return await connection.QuerySingleAsync<bool>(
            "select is_active from user_companies where user_id = @userId and company_id = @companyId", new { userId, companyId });
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text.Length == 0 ? "{}" : text);
        return document.RootElement.Clone();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await ReadAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;

    private static async Task<string> CodeAndDetailAsync(HttpResponseMessage response)
    {
        var body = await ReadAsync(response);
        var code = body.TryGetProperty("code", out var value) ? value.GetString() : null;
        var detail = body.TryGetProperty("detail", out var text) ? text.GetString() : null;
        return $"{code}|{detail}";
    }
}

/// <summary>
/// Pauses the next <c>expected</c> calls to <see cref="ICompanyMemberStore.ChangeAsync"/> (for one target user, or any)
/// before or after the real change, until the test opens it.
/// </summary>
internal sealed class MemberStoreGate
{
    public enum Mode { Before, After }

    private SemaphoreSlim _reached = new(0);
    private TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _expected;
    private int _armed;

    public Guid? Target { get; private set; }

    public Mode Current { get; private set; }

    public void Arm(Guid? target, Mode mode, int expected)
    {
        Target = target;
        Current = mode;
        _expected = expected;
        _reached = new SemaphoreSlim(0);
        _open = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _armed, expected);
    }

    public bool TryEnter(Guid userId) =>
        Volatile.Read(ref _armed) > 0 && (Target is null || Target == userId) && Interlocked.Decrement(ref _armed) >= 0;

    public async Task PauseAsync()
    {
        _reached.Release();
        await _open.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public async Task WaitReachedAsync()
    {
        for (var i = 0; i < _expected; i++)
        {
            Assert.That(await _reached.WaitAsync(TimeSpan.FromSeconds(30)), Is.True, "The gate was never reached.");
        }
    }

    public void Open()
    {
        Volatile.Write(ref _armed, 0);
        _open.TrySetResult();
    }
}

internal sealed class GatedMemberStore(ICompanyMemberStore inner, MemberStoreGate gate) : ICompanyMemberStore
{
    public Task<Guid?> FindCompanyIdAsync(string nit, CancellationToken ct) => inner.FindCompanyIdAsync(nit, ct);

    public Task<IReadOnlyList<CompanyMember>> ListAsync(Guid companyId, CancellationToken ct) => inner.ListAsync(companyId, ct);

    public Task<IReadOnlyList<Guid>> ListActiveUserIdsAsync(Guid companyId, CancellationToken ct) => inner.ListActiveUserIdsAsync(companyId, ct);

    public async Task<CompanyMemberChange> ChangeAsync(Guid companyId, Guid userId, string role, bool isActive, CancellationToken ct)
    {
        var enter = gate.TryEnter(userId);
        if (enter && gate.Current == MemberStoreGate.Mode.Before)
        {
            await gate.PauseAsync();
        }

        var changed = await inner.ChangeAsync(companyId, userId, role, isActive, ct);
        if (enter && gate.Current == MemberStoreGate.Mode.After)
        {
            await gate.PauseAsync();
        }

        return changed;
    }
}
