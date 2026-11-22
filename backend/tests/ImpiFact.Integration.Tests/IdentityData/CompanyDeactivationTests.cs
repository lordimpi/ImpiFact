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
/// <c>POST /api/v1/platform/companies/{nit}/deactivate</c> and <c>/reactivate</c> (phase 7a, T9b1; decision L17): only the
/// platform, never a hard delete. Deactivating revokes the API keys and the panel sessions of the company's users in one
/// transaction; its users cannot log into or select it (the selector still lists it, disabled); reactivating lets them in
/// again without bringing back the revoked keys. Repeating either call answers 200 with <c>changed</c> false.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class CompanyDeactivationTests
{
    private const string Password = "Contraseña-Segura-1";

    private IdentityDataApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi() => _factory = new IdentityDataApiFactory();

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    [Test]
    public async Task Only_the_platform_deactivates_or_reactivates_and_an_unknown_nit_is_404()
    {
        var company = await AddCompanyAsync();
        var owner = await AddUserAsync(company.Id, RoleNames.Owner);
        var seller = await AddUserAsync(company.Id, RoleNames.Seller);
        using var byOwner = await ClientAsync(owner.Email, company.Id);
        using var bySeller = await ClientAsync(seller.Email, company.Id);
        using var key = await CompanyKeyAsync(byOwner);
        using var admin = await PlatformAdminAsync();

        using var ownerDeactivates = await byOwner.PostAsync(Deactivate(company.Nit), null);
        using var sellerDeactivates = await bySeller.PostAsync(Deactivate(company.Nit), null);
        using var keyDeactivates = await key.PostAsync(Deactivate(company.Nit), null);
        using var ownerReactivates = await byOwner.PostAsync(Reactivate(company.Nit), null);
        using var unknown = await admin.PostAsync(Deactivate(MissingNit()), null);
        using var unknownReactivate = await admin.PostAsync(Reactivate(MissingNit()), null);

        Assert.Multiple(async () =>
        {
            Assert.That(ownerDeactivates.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(sellerDeactivates.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(keyDeactivates.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(ownerReactivates.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(await CodeAsync(unknown), Is.EqualTo("company.not_found"));
            Assert.That(unknownReactivate.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(await CompanyIsActiveAsync(company.Id), Is.True, "Nobody but the platform changed the company.");
            Assert.That(await CountAsync("select count(*) from api_keys where company_id = @id and revoked_at is not null", new { id = company.Id }), Is.Zero);
        });
    }

    [Test]
    public async Task Deactivating_revokes_the_keys_and_sessions_and_the_users_cannot_log_into_or_select_the_company()
    {
        var company = await AddCompanyAsync();
        var other = await AddCompanyAsync();
        var owner = await AddUserAsync(company.Id, RoleNames.Owner);
        var seller = await AddUserAsync(company.Id, RoleNames.Seller);
        await JoinAsync(seller.Id, other.Id, RoleNames.Seller);
        var outsider = await AddUserAsync(other.Id, RoleNames.Owner);
        using var byOwner = await ClientAsync(owner.Email, company.Id);
        using var key = await CompanyKeyAsync(byOwner);
        using (var before = await key.GetAsync($"/api/v1/companies/{company.Nit}/users"))
        {
            Assert.That(before.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized), "The key works before the change.");
        }

        var sellerSession = await ReadAsync(await LoginAsync(seller.Email, company.Id));
        var outsiderSession = await ReadAsync(await LoginAsync(outsider.Email, other.Id));
        using var admin = await PlatformAdminAsync();

        using var deactivated = await admin.PostAsync(Deactivate(company.Nit), null);
        var body = await ReadAsync(deactivated);

        using var keyAfter = await key.GetAsync($"/api/v1/companies/{company.Nit}/users");
        using var ownerCreatesKey = await byOwner.PostAsJsonAsync("/api/v1/api-keys", new { name = "otra", scopes = new[] { "documents:read" } });
        using var loginCompany = await LoginAsync(owner.Email, company.Id);
        using var loginWithout = await LoginAsync(owner.Email, companyId: null);
        var withoutCompany = await ReadAsync(loginWithout);
        using var sellerLogin = await LoginAsync(seller.Email, companyId: null);
        var sellerAfter = await ReadAsync(sellerLogin);
        using var selectCompany = await RefreshAsync(sellerAfter.GetProperty("refresh_token").GetString()!, company.Id);
        using var oldSellerRefresh = await RefreshAsync(sellerSession.GetProperty("refresh_token").GetString()!, company.Id);
        using var outsiderRefresh = await RefreshAsync(outsiderSession.GetProperty("refresh_token").GetString()!, other.Id);
        using var inOther = _factory.CreateClient(sellerAfter.GetProperty("access_token").GetString()!);
        using var myCompanies = await inOther.GetAsync("/api/v1/me/companies");
        var listed = Items(await myCompanies.Content.ReadAsStringAsync());
        using var platformList = await admin.GetAsync($"/api/v1/platform/companies?search={company.Nit}");
        var platformItems = Items(await platformList.Content.ReadAsStringAsync());

        Assert.Multiple(async () =>
        {
            Assert.That(deactivated.StatusCode, Is.EqualTo(HttpStatusCode.OK), body.GetRawText());
            Assert.That(body.GetProperty("id").GetGuid(), Is.EqualTo(company.Id));
            Assert.That(body.GetProperty("nit").GetString(), Is.EqualTo(company.Nit));
            Assert.That(body.GetProperty("is_active").GetBoolean(), Is.False);
            Assert.That(body.GetProperty("changed").GetBoolean(), Is.True);
            Assert.That(body.GetProperty("revoked_api_keys").GetInt32(), Is.EqualTo(1));
            Assert.That(body.GetProperty("signed_out_users").GetInt32(), Is.EqualTo(2), "The owner and the seller.");
            Assert.That(await CompanyIsActiveAsync(company.Id), Is.False);

            Assert.That(keyAfter.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "The company API keys are revoked.");
            Assert.That(await CountAsync("select count(*) from api_keys where company_id = @id and revoked_at is null", new { id = company.Id }), Is.Zero);
            Assert.That(ownerCreatesKey.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "An access token still alive gives no owner operation.");
            Assert.That(await CountAsync("select count(*) from refresh_tokens where user_id = @id and revoked_at is null", new { id = owner.Id }),
                Is.EqualTo(1), "Only the session opened after the change (login without company).");
            Assert.That(oldSellerRefresh.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "The sessions open before the change were revoked.");
            Assert.That(outsiderRefresh.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Users of other companies keep their sessions.");

            Assert.That(loginCompany.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(await CodeAsync(loginCompany), Is.EqualTo("identity.company_inactive"));
            Assert.That(loginWithout.StatusCode, Is.EqualTo(HttpStatusCode.OK), withoutCompany.GetRawText());
            Assert.That(withoutCompany.GetProperty("company_id").ValueKind, Is.EqualTo(JsonValueKind.Null), "Its only company is inactive: none is chosen.");
            Assert.That(withoutCompany.GetProperty("companies").EnumerateArray().Select(item => item.GetGuid()), Is.EqualTo(new[] { company.Id }),
                "The session still lists it, so the selector shows it disabled.");
            Assert.That(sellerAfter.GetProperty("company_id").GetGuid(), Is.EqualTo(other.Id), "The only active company is chosen.");
            Assert.That(selectCompany.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(await CodeAsync(selectCompany), Is.EqualTo("identity.company_inactive"));
            Assert.That(listed.Select(item => (item.GetProperty("id").GetGuid(), item.GetProperty("is_active").GetBoolean())),
                Is.EquivalentTo(new[] { (company.Id, false), (other.Id, true) }));
            Assert.That(platformItems.Single().GetProperty("is_active").GetBoolean(), Is.False, "The platform keeps reading the company.");
            Assert.That(await MembershipActiveAsync(seller.Id, company.Id), Is.True, "Memberships are untouched.");
        });
    }

    [Test]
    public async Task Deactivating_twice_or_reactivating_an_active_company_changes_nothing()
    {
        var company = await AddCompanyAsync();
        var owner = await AddUserAsync(company.Id, RoleNames.Owner);
        using var admin = await PlatformAdminAsync();

        using var alreadyActive = await admin.PostAsync(Reactivate(company.Nit), null);
        var activeBody = await ReadAsync(alreadyActive);
        using (var first = await admin.PostAsync(Deactivate(company.Nit), null))
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // A key and a session created while it is inactive would be revoked again by a second real deactivation.
        var session = await ReadAsync(await LoginAsync(owner.Email, companyId: null));
        using var again = await admin.PostAsync(Deactivate(company.Nit), null);
        var againBody = await ReadAsync(again);
        using var refresh = await RefreshAsync(session.GetProperty("refresh_token").GetString()!, companyId: null);

        Assert.Multiple(() =>
        {
            Assert.That(alreadyActive.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(activeBody.GetProperty("changed").GetBoolean(), Is.False);
            Assert.That(activeBody.GetProperty("is_active").GetBoolean(), Is.True);
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(againBody.GetProperty("changed").GetBoolean(), Is.False);
            Assert.That(againBody.GetProperty("is_active").GetBoolean(), Is.False);
            Assert.That(againBody.GetProperty("revoked_api_keys").GetInt32(), Is.Zero);
            Assert.That(againBody.GetProperty("signed_out_users").GetInt32(), Is.Zero);
            Assert.That(refresh.StatusCode, Is.EqualTo(HttpStatusCode.OK), "The repeated call closed no session.");
        });
    }

    [Test]
    public async Task Reactivating_lets_the_users_in_again_but_never_brings_back_the_revoked_keys()
    {
        var company = await AddCompanyAsync();
        var owner = await AddUserAsync(company.Id, RoleNames.Owner);
        using var byOwner = await ClientAsync(owner.Email, company.Id);
        using var key = await CompanyKeyAsync(byOwner);
        using var admin = await PlatformAdminAsync();
        using (var deactivated = await admin.PostAsync(Deactivate(company.Nit), null))
        {
            Assert.That(deactivated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        using var reactivated = await admin.PostAsync(Reactivate(company.Nit), null);
        var body = await ReadAsync(reactivated);
        using var login = await LoginAsync(owner.Email, company.Id);
        using var keyAfter = await key.GetAsync($"/api/v1/companies/{company.Nit}/users");
        using var byOwnerAgain = await ClientAsync(owner.Email, company.Id);
        using var newKey = await byOwnerAgain.PostAsJsonAsync("/api/v1/api-keys", new { name = "nueva", scopes = new[] { "config:write" } });

        Assert.Multiple(async () =>
        {
            Assert.That(reactivated.StatusCode, Is.EqualTo(HttpStatusCode.OK), body.GetRawText());
            Assert.That(body.GetProperty("changed").GetBoolean(), Is.True);
            Assert.That(body.GetProperty("is_active").GetBoolean(), Is.True);
            Assert.That(body.GetProperty("revoked_api_keys").GetInt32(), Is.Zero);
            Assert.That(await CompanyIsActiveAsync(company.Id), Is.True);
            Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK), "The users can log into the company again.");
            Assert.That(keyAfter.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "Revoked keys stay revoked.");
            Assert.That(newKey.StatusCode, Is.EqualTo(HttpStatusCode.Created), "An owner creates new keys.");
        });
    }

    private static string Deactivate(string nit) => $"/api/v1/platform/companies/{nit}/deactivate";

    private static string Reactivate(string nit) => $"/api/v1/platform/companies/{nit}/reactivate";

    private static string MissingNit() =>
        Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture) + "9";

    private static async Task<(Guid Id, string Nit)> AddCompanyAsync()
    {
        var id = await IdentityDatabase.AddCompanyAsync();
        await using var connection = TestDatabase.Connect(IdentityDatabase.ConnectionString);
        return (id, await connection.QuerySingleAsync<string>("select identification_number from companies where id = @id", new { id }));
    }

    private static async Task<(Guid Id, string Email)> AddUserAsync(Guid companyId, string role)
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
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

    private async Task<HttpClient> PlatformAdminAsync()
    {
        var email = $"admin-{Guid.NewGuid():N}@example.com";
        await IdentityStoreTests.AddUserAsync([RoleNames.PlatformAdmin], company: null, email, Password);
        return await ClientAsync(email, companyId: null);
    }

    private async Task<HttpClient> CompanyKeyAsync(HttpClient owner)
    {
        using var created = await owner.PostAsJsonAsync("/api/v1/api-keys", new { name = "integracion", scopes = new[] { "config:write" } });
        var body = await ReadAsync(created);
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), body.GetRawText());
        return _factory.CreateClient(body.GetProperty("key").GetString()!);
    }

    private async Task<HttpClient> ClientAsync(string email, Guid? companyId)
    {
        using var response = await LoginAsync(email, companyId);
        var tokens = await ReadAsync(response);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), tokens.GetRawText());
        return _factory.CreateClient(tokens.GetProperty("access_token").GetString()!);
    }

    private Task<HttpResponseMessage> LoginAsync(string email, Guid? companyId) =>
        _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password, company_id = companyId });

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken, Guid? companyId) =>
        _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refresh_token = refreshToken, company_id = companyId });

    private static async Task<bool> CompanyIsActiveAsync(Guid companyId)
    {
        await using var connection = TestDatabase.Connect(IdentityDatabase.ConnectionString);
        return await connection.QuerySingleAsync<bool>("select is_active from companies where id = @companyId", new { companyId });
    }

    private static async Task<bool> MembershipActiveAsync(Guid userId, Guid companyId)
    {
        await using var connection = TestDatabase.Connect(IdentityDatabase.ConnectionString);
        return await connection.QuerySingleAsync<bool>(
            "select is_active from user_companies where user_id = @userId and company_id = @companyId", new { userId, companyId });
    }

    private static async Task<long> CountAsync(string sql, object parameters)
    {
        await using var connection = TestDatabase.Connect(IdentityDatabase.ConnectionString);
        return await connection.QuerySingleAsync<long>(sql, parameters);
    }

    private static List<JsonElement> Items(string text)
    {
        using var document = JsonDocument.Parse(text.Length == 0 ? "{}" : text);
        return document.RootElement.TryGetProperty("items", out var items) ? [.. items.EnumerateArray().Select(item => item.Clone())] : [];
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text.Length == 0 ? "{}" : text);
        return document.RootElement.Clone();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await ReadAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;
}
