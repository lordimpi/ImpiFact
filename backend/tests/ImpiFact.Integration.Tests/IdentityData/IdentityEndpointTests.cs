using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Identity;
using ImpiFact.Infrastructure.Identity;
using ImpiFact.Integration.Tests.Identity;
using Npgsql;

namespace ImpiFact.Integration.Tests.IdentityData;

/// <summary>
/// Endpoints de Identity de punta a punta, contra PostgreSQL: inicio de sesión, renovación rotativa con detección de
/// reúso, API keys (crear, autenticar, revocar) y usuarios. Revisa también que ninguna credencial quede en la base en
/// claro ni en los registros.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class IdentityEndpointTests
{
    private const string Password = "Contraseña-Segura-1";

    private IdentityDataApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi() => _factory = new IdentityDataApiFactory();

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    [Test]
    public async Task Login_returns_tokens_that_authenticate_the_user_in_the_company()
    {
        var companyId = await IdentityDatabase.AddCompanyAsync();
        var (userId, email) = await AddUserAsync(new CompanyMembership(companyId, RoleNames.Owner));

        using var response = await Login(email.ToUpperInvariant(), Password);
        var tokens = await ReadAsync(response);
        using var client = _factory.CreateClient(tokens.GetProperty("access_token").GetString()!);
        var whoAmI = await client.GetFromJsonAsync<TestEndpoints.WhoAmI>("/test/whoami");
        using var configWrite = await client.GetAsync("/test/config-write");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(tokens.GetProperty("token_type").GetString(), Is.EqualTo("Bearer"));
            Assert.That(tokens.GetProperty("company_id").GetGuid(), Is.EqualTo(companyId));
            Assert.That(tokens.GetProperty("refresh_token").GetString(), Is.Not.Empty);
            Assert.That(whoAmI, Is.EqualTo(new TestEndpoints.WhoAmI(companyId, userId, "User")));
            Assert.That(configWrite.StatusCode, Is.EqualTo(HttpStatusCode.OK), "An owner token carries config:write.");
        });
    }

    [Test]
    public async Task Unknown_email_and_wrong_password_answer_the_same_401()
    {
        var (_, email) = await AddUserAsync(company: null);

        using var unknown = await Login("nadie-" + email, Password);
        using var wrong = await Login(email, "otra-contraseña");
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        var wrongBody = await wrong.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(wrong.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(Code(unknownBody), Is.EqualTo("identity.invalid_credentials"));
            Assert.That(Code(wrongBody), Is.EqualTo("identity.invalid_credentials"));
            Assert.That(Message(unknownBody), Is.EqualTo(Message(wrongBody)));
        });
    }

    [Test]
    public async Task Login_with_missing_fields_answers_422_with_spanish_messages()
    {
        using var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = "" });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(body.RootElement.GetProperty("errors").GetProperty("email")[0].GetString(), Is.EqualTo("El correo es obligatorio."));
            Assert.That(body.RootElement.GetProperty("errors").GetProperty("password")[0].GetString(), Is.EqualTo("La contraseña es obligatoria."));
        });
    }

    [Test]
    public async Task Refresh_rotates_and_a_reused_token_revokes_every_active_token_of_the_user()
    {
        var (userId, email) = await AddUserAsync(company: null);
        var first = await ReadAsync(await Login(email, Password));
        var original = first.GetProperty("refresh_token").GetString()!;

        using var rotatedResponse = await Refresh(original);
        var rotated = await ReadAsync(rotatedResponse);
        var replacement = rotated.GetProperty("refresh_token").GetString()!;
        var oldRow = await QuerySingleAsync<(DateTime? RevokedAt, Guid? ReplacedBy)>(
            "select revoked_at, replaced_by from refresh_tokens where token_hash = @hash", new { hash = CredentialHash.Sha256Hex(original) });
        var newId = await QuerySingleAsync<Guid>("select id from refresh_tokens where token_hash = @hash", new { hash = CredentialHash.Sha256Hex(replacement) });

        using var reused = await Refresh(original);
        using var afterTheft = await Refresh(replacement);
        var active = await QuerySingleAsync<long>(
            "select count(*) from refresh_tokens where user_id = @userId and revoked_at is null", new { userId });

        Assert.Multiple(() =>
        {
            Assert.That(rotatedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(replacement, Is.Not.EqualTo(original));
            Assert.That(oldRow.RevokedAt, Is.Not.Null);
            Assert.That(oldRow.ReplacedBy, Is.EqualTo(newId));
            Assert.That(reused.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(afterTheft.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "The reuse also revoked the replacement.");
            Assert.That(active, Is.Zero);
        });
    }

    [Test]
    public async Task An_owner_creates_an_api_key_that_authenticates_and_stops_working_right_after_revocation()
    {
        var companyId = await IdentityDatabase.AddCompanyAsync();
        var (_, email) = await AddUserAsync(new CompanyMembership(companyId, RoleNames.Owner));
        using var owner = _factory.CreateClient(await AccessTokenAsync(email));

        using var created = await owner.PostAsJsonAsync("/api/v1/api-keys", new { name = "ERP", scopes = new[] { ApiScopes.DocumentsWrite } });
        var key = await ReadAsync(created);
        var plainText = key.GetProperty("key").GetString()!;
        var id = key.GetProperty("id").GetGuid();
        var row = await QuerySingleAsync<(string KeyHash, Guid CompanyId, string Scopes)>(
            "select key_hash, company_id, scopes from api_keys where id = @id", new { id });

        using var withKey = _factory.CreateClient(plainText);
        var whoAmI = await withKey.GetFromJsonAsync<TestEndpoints.WhoAmI>("/test/whoami");
        using var revoked = await owner.DeleteAsync($"/api/v1/api-keys/{id}");
        using var afterRevocation = await withKey.GetAsync("/test/whoami");
        var revokedAt = await QuerySingleAsync<DateTime?>("select revoked_at from api_keys where id = @id", new { id });

        Assert.Multiple(() =>
        {
            Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(created.Headers.Location?.ToString(), Is.EqualTo($"/api/v1/api-keys/{id}"));
            Assert.That(created.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(plainText, Does.StartWith("apk_"));
            Assert.That(row.KeyHash, Is.EqualTo(CredentialHash.Sha256Hex(plainText)));
            Assert.That(row.CompanyId, Is.EqualTo(companyId));
            Assert.That(row.Scopes, Is.EqualTo(ApiScopes.DocumentsWrite));
            Assert.That(whoAmI, Is.EqualTo(new TestEndpoints.WhoAmI(companyId, null, "ApiKey")));
            Assert.That(revoked.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(revokedAt, Is.Not.Null);
            Assert.That(afterRevocation.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "The cache entry is cleared on revocation.");
            Assert.That(_factory.Logs.Entries, Has.None.Contains(plainText));
        });
    }

    [Test]
    public async Task A_key_of_another_company_cannot_be_revoked_and_is_reported_as_missing()
    {
        var ownCompany = await IdentityDatabase.AddCompanyAsync();
        var otherCompany = await IdentityDatabase.AddCompanyAsync();
        var (_, ownerEmail) = await AddUserAsync(new CompanyMembership(ownCompany, RoleNames.Owner));
        var (_, otherEmail) = await AddUserAsync(new CompanyMembership(otherCompany, RoleNames.Owner));
        using var other = _factory.CreateClient(await AccessTokenAsync(otherEmail));
        var foreignKey = await ReadAsync(await other.PostAsJsonAsync("/api/v1/api-keys", new { name = "Otra", scopes = new[] { ApiScopes.DocumentsRead } }));
        using var owner = _factory.CreateClient(await AccessTokenAsync(ownerEmail));

        using var response = await owner.DeleteAsync($"/api/v1/api-keys/{foreignKey.GetProperty("id").GetGuid()}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(Code(body), Is.EqualTo("api_key.not_found"));
        });
    }

    [Test]
    public async Task Credential_endpoints_need_config_write_or_platform()
    {
        var companyId = await IdentityDatabase.AddCompanyAsync();
        var (_, sellerEmail) = await AddUserAsync(new CompanyMembership(companyId, RoleNames.Seller));
        using var seller = _factory.CreateClient(await AccessTokenAsync(sellerEmail));
        using var anonymous = _factory.CreateClient();

        using var bySeller = await seller.PostAsJsonAsync("/api/v1/api-keys", new { name = "ERP", scopes = new[] { ApiScopes.DocumentsRead } });
        using var userBySeller = await seller.PostAsJsonAsync("/api/v1/users", new { email = "x@example.com", password = Password, name = "X", role = "seller" });
        using var byAnonymous = await anonymous.PostAsJsonAsync("/api/v1/api-keys", new { name = "ERP", scopes = new[] { ApiScopes.DocumentsRead } });

        Assert.Multiple(() =>
        {
            Assert.That(bySeller.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(userBySeller.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(byAnonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        });
    }

    [Test]
    public async Task A_platform_admin_creates_a_platform_key_that_acts_as_platform()
    {
        var (_, email) = await AddUserAsync(company: null, platformRoles: [RoleNames.PlatformAdmin]);
        using var admin = _factory.CreateClient(await AccessTokenAsync(email));

        using var created = await admin.PostAsJsonAsync("/api/v1/api-keys", new { name = "Plataforma", scopes = new[] { ApiScopes.Platform } });
        using var mixed = await admin.PostAsJsonAsync("/api/v1/api-keys", new { name = "Mezcla", scopes = new[] { ApiScopes.Platform, ApiScopes.DocumentsRead } });
        var key = await ReadAsync(created);
        using var withKey = _factory.CreateClient(key.GetProperty("key").GetString()!);
        var whoAmI = await withKey.GetFromJsonAsync<TestEndpoints.WhoAmI>("/test/whoami");

        Assert.Multiple(() =>
        {
            Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(key.GetProperty("company_id").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(mixed.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(whoAmI, Is.EqualTo(new TestEndpoints.WhoAmI(null, null, "Platform")));
        });
    }

    [Test]
    public async Task An_owner_creates_a_seller_who_can_log_in_and_a_repeated_email_is_a_conflict()
    {
        var companyId = await IdentityDatabase.AddCompanyAsync();
        var (_, ownerEmail) = await AddUserAsync(new CompanyMembership(companyId, RoleNames.Owner));
        using var owner = _factory.CreateClient(await AccessTokenAsync(ownerEmail));
        var email = $"vendedor-{Guid.NewGuid():N}@example.com";

        using var created = await owner.PostAsJsonAsync("/api/v1/users", new { email, password = Password, name = "Vendedor", role = "seller" });
        var user = await ReadAsync(created);
        using var repeated = await owner.PostAsJsonAsync("/api/v1/users", new { email = email.ToUpperInvariant(), password = Password, name = "Otro", role = "seller" });
        var login = await ReadAsync(await Login(email, Password));
        var storedHash = await QuerySingleAsync<string>("select password_hash from users where email = @email", new { email });

        Assert.Multiple(() =>
        {
            Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(user.GetProperty("company_id").GetGuid(), Is.EqualTo(companyId));
            Assert.That(user.GetProperty("role").GetString(), Is.EqualTo("seller"));
            Assert.That(user.TryGetProperty("password", out _), Is.False);
            Assert.That(repeated.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(login.GetProperty("company_id").GetGuid(), Is.EqualTo(companyId));
            Assert.That(login.GetProperty("roles").EnumerateArray().Select(role => role.GetString()), Is.EqualTo(new[] { "seller" }));
            Assert.That(storedHash, Does.Not.Contain(Password));
            Assert.That(_factory.Logs.Entries, Has.None.Contains(Password));
        });
    }

    private async Task<HttpResponseMessage> Login(string email, string password) =>
        await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    private async Task<HttpResponseMessage> Refresh(string refreshToken) =>
        await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refresh_token = refreshToken });

    private async Task<string> AccessTokenAsync(string email) =>
        (await ReadAsync(await Login(email, Password))).GetProperty("access_token").GetString()!;

    private static async Task<(Guid Id, string Email)> AddUserAsync(CompanyMembership? company, IReadOnlyList<string>? platformRoles = null)
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var id = await IdentityStoreTests.AddUserAsync(platformRoles ?? [], company, email, Password);
        return (id, email);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string? Code(string body) => JsonDocument.Parse(body).RootElement.GetProperty("code").GetString();

    private static string? Message(string body) => JsonDocument.Parse(body).RootElement.GetProperty("message").GetString();

    private static async Task<T> QuerySingleAsync<T>(string sql, object parameters)
    {
        await using var connection = new NpgsqlConnection(IdentityDatabase.ConnectionString);
        return await connection.QuerySingleAsync<T>(sql, parameters);
    }
}
