using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Identity;
using ImpiFact.Infrastructure.Identity;

namespace ImpiFact.Integration.Tests.IdentityData;

/// <summary>
/// <c>GET /api/v1/api-keys</c> contra PostgreSQL (Arquitectura frontend, sección 16, punto 13): las llaves de la empresa
/// activa, o las de plataforma para la plataforma, de la más nueva a la más antigua y por cursor. Nunca trae el texto de
/// una llave ni su resumen, ni llaves de otra empresa.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class ApiKeyListTests
{
    private const string Password = "Contraseña-Segura-1";

    private IdentityDataApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi() => _factory = new IdentityDataApiFactory();

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    [Test]
    public async Task An_owner_lists_the_company_keys_newest_first_by_cursor_without_any_secret()
    {
        var companyId = await IdentityDatabase.AddCompanyAsync();
        var otherCompany = await IdentityDatabase.AddCompanyAsync();
        using var owner = _factory.CreateClient(await OwnerTokenAsync(companyId));
        using var otherOwner = _factory.CreateClient(await OwnerTokenAsync(otherCompany));
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);
        var first = await CreateKeyAsync(owner, new { name = "ERP", scopes = new[] { ApiScopes.DocumentsWrite, ApiScopes.DocumentsRead } });
        var second = await CreateKeyAsync(owner, new { name = "Tienda", scopes = new[] { ApiScopes.DocumentsRead }, expires_at = expiresAt });
        var third = await CreateKeyAsync(owner, new { name = "Contabilidad", scopes = new[] { ApiScopes.DocumentsRead } });
        var foreign = await CreateKeyAsync(otherOwner, new { name = "Ajena", scopes = new[] { ApiScopes.DocumentsRead } });
        (await owner.DeleteAsync($"/api/v1/api-keys/{third.Id}")).Dispose();

        using var firstPage = await owner.GetAsync("/api/v1/api-keys?limit=2");
        var firstBody = await firstPage.Content.ReadAsStringAsync();
        var page1 = JsonDocument.Parse(firstBody).RootElement;
        var cursor = page1.GetProperty("next_cursor").GetString();
        using var secondPage = await owner.GetAsync($"/api/v1/api-keys?limit=2&cursor={Uri.EscapeDataString(cursor ?? "")}");
        var secondBody = await secondPage.Content.ReadAsStringAsync();
        var page2 = JsonDocument.Parse(secondBody).RootElement;

        Assert.That(firstPage.StatusCode, Is.EqualTo(HttpStatusCode.OK), firstBody);
        var items = page1.GetProperty("items").EnumerateArray().Concat(page2.GetProperty("items").EnumerateArray()).ToList();
        var revoked = items[0];
        var expiring = items[1];
        var oldest = items[2];
        Assert.Multiple(() =>
        {
            Assert.That(secondPage.StatusCode, Is.EqualTo(HttpStatusCode.OK), secondBody);
            Assert.That(items.Select(item => item.GetProperty("id").GetGuid()), Is.EqualTo(new[] { third.Id, second.Id, first.Id }), "Newest first.");
            Assert.That(page2.GetProperty("next_cursor").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(revoked.GetProperty("name").GetString(), Is.EqualTo("Contabilidad"));
            Assert.That(revoked.GetProperty("is_revoked").GetBoolean(), Is.True);
            Assert.That(revoked.GetProperty("revoked_at").ValueKind, Is.EqualTo(JsonValueKind.String));
            Assert.That(expiring.GetProperty("is_revoked").GetBoolean(), Is.False);
            Assert.That(expiring.GetProperty("revoked_at").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(expiring.GetProperty("expires_at").GetDateTimeOffset(), Is.EqualTo(expiresAt).Within(TimeSpan.FromMilliseconds(1)));
            Assert.That(oldest.GetProperty("key_prefix").GetString(), Is.EqualTo(first.Prefix));
            Assert.That(oldest.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()),
                Is.EqualTo(new[] { ApiScopes.DocumentsWrite, ApiScopes.DocumentsRead }));
            Assert.That(oldest.GetProperty("created_at").ValueKind, Is.EqualTo(JsonValueKind.String));
            Assert.That(oldest.TryGetProperty("last_used_at", out _), Is.True);
            Assert.That(oldest.GetProperty("expires_at").ValueKind, Is.EqualTo(JsonValueKind.Null));
            foreach (var body in new[] { firstBody, secondBody })
            {
                Assert.That(body, Does.Not.Contain(first.PlainText).And.Not.Contain(CredentialHash.Sha256Hex(first.PlainText)));
                Assert.That(body, Does.Not.Contain("\"key\"").And.Not.Contain("key_hash"));
                Assert.That(body, Does.Not.Contain(foreign.Id.ToString()), "Keys of another company never appear.");
            }
        });
    }

    [Test]
    public async Task A_platform_admin_lists_platform_keys_and_no_company_key()
    {
        var companyId = await IdentityDatabase.AddCompanyAsync();
        using var owner = _factory.CreateClient(await OwnerTokenAsync(companyId));
        var companyKey = await CreateKeyAsync(owner, new { name = "De empresa", scopes = new[] { ApiScopes.DocumentsRead } });
        var adminId = await IdentityStoreTests.AddUserAsync([RoleNames.PlatformAdmin], company: null, Email(), Password);
        using var admin = _factory.CreateClient(await AccessTokenAsync(await EmailOfAsync(adminId), companyId: null));
        var platformKey = await CreateKeyAsync(admin, new { name = "Plataforma", scopes = new[] { ApiScopes.Platform } });

        using var response = await admin.GetAsync("/api/v1/api-keys?limit=100");
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        var ids = JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Contain(platformKey.Id));
            Assert.That(ids, Does.Not.Contain(companyKey.Id));
            Assert.That(body, Does.Not.Contain(platformKey.PlainText));
        });
    }

    [Test]
    public async Task A_seller_or_a_user_without_platform_role_cannot_list_keys()
    {
        var companyId = await IdentityDatabase.AddCompanyAsync();
        var sellerId = await IdentityStoreTests.AddUserAsync([], new CompanyMembership(companyId, RoleNames.Seller), Email(), Password);
        var plainId = await IdentityStoreTests.AddUserAsync([], company: null, Email(), Password);
        using var seller = _factory.CreateClient(await AccessTokenAsync(await EmailOfAsync(sellerId), companyId));
        using var plain = _factory.CreateClient(await AccessTokenAsync(await EmailOfAsync(plainId), companyId: null));

        using var bySeller = await seller.GetAsync("/api/v1/api-keys");
        using var byPlain = await plain.GetAsync("/api/v1/api-keys");
        using var anonymous = await _factory.CreateClient().GetAsync("/api/v1/api-keys");

        Assert.Multiple(() =>
        {
            Assert.That(bySeller.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(byPlain.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        });
    }

    [Test]
    public async Task A_wrong_cursor_or_limit_answers_422_with_spanish_messages()
    {
        using var owner = _factory.CreateClient(await OwnerTokenAsync(await IdentityDatabase.AddCompanyAsync()));

        using var badCursor = await owner.GetAsync("/api/v1/api-keys?cursor=no-es-un-cursor");
        using var badLimit = await owner.GetAsync("/api/v1/api-keys?limit=101");
        var cursorErrors = JsonDocument.Parse(await badCursor.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        var limitErrors = JsonDocument.Parse(await badLimit.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");

        Assert.Multiple(() =>
        {
            Assert.That(badCursor.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(cursorErrors.GetProperty("cursor")[0].GetString(), Is.EqualTo("cursor es inválido."));
            Assert.That(badLimit.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(limitErrors.GetProperty("limit")[0].GetString(), Is.EqualTo("El campo limit debe ser como máximo 100."));
        });
    }

    private static async Task<CreatedKey> CreateKeyAsync(HttpClient client, object body)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/api-keys", body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), text);
        var root = JsonDocument.Parse(text).RootElement;
        return new CreatedKey(root.GetProperty("id").GetGuid(), root.GetProperty("key").GetString()!, root.GetProperty("key_prefix").GetString()!);
    }

    private async Task<string> OwnerTokenAsync(Guid companyId)
    {
        var userId = await IdentityStoreTests.AddUserAsync([], new CompanyMembership(companyId, RoleNames.Owner), Email(), Password);
        return await AccessTokenAsync(await EmailOfAsync(userId), companyId);
    }

    private async Task<string> AccessTokenAsync(string email, Guid? companyId)
    {
        using var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/login", new { email, password = Password, company_id = companyId });
        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        return JsonDocument.Parse(text).RootElement.GetProperty("access_token").GetString()!;
    }

    private static async Task<string> EmailOfAsync(Guid userId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(IdentityDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("select email from users where id = @id", connection);
        command.Parameters.AddWithValue("id", userId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static string Email() => $"user-{Guid.NewGuid():N}@example.com";

    private sealed record CreatedKey(Guid Id, string PlainText, string Prefix);
}
