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
/// The users of a company, managed by its owner (or by the platform): <c>GET /api/v1/companies/{nit}/users</c>, user
/// creation with <c>POST /api/v1/users</c> and <c>PATCH /api/v1/companies/{nit}/users/{userId}</c> (role and active
/// membership). Another company answers the same as a NIT that does not exist, and a deactivated membership can no
/// longer log into, select or refresh into that company.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class CompanyUsersTests
{
    private const string Password = "Contraseña-Segura-1";

    private IdentityDataApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi() => _factory = new IdentityDataApiFactory();

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    [Test]
    public async Task The_owner_lists_the_company_users_by_name_then_email_without_secrets()
    {
        var company = await AddCompanyAsync();
        var other = await AddCompanyAsync();
        var owner = await AddUserAsync(company.Id, RoleNames.Owner, "Ana Dueña", "3001112233");
        var sellerB = await AddUserAsync(company.Id, RoleNames.Seller, "Bruno Vendedor", email: $"b-{Guid.NewGuid():N}@example.com");
        var sellerA = await AddUserAsync(company.Id, RoleNames.Seller, "Bruno Vendedor", email: $"a-{Guid.NewGuid():N}@example.com");
        var stranger = await AddUserAsync(other.Id, RoleNames.Owner, "Alguien Ajeno");
        using var client = await ClientAsync(owner.Email, company.Id);

        using var response = await client.GetAsync($"/api/v1/companies/{company.Nit}/users");
        var text = await response.Content.ReadAsStringAsync();
        var items = Items(text);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
            Assert.That(items.Select(item => item.GetProperty("id").GetGuid()), Is.EqualTo(new[] { owner.Id, sellerA.Id, sellerB.Id }));
            Assert.That(items[0].GetProperty("email").GetString(), Is.EqualTo(owner.Email));
            Assert.That(items[0].GetProperty("name").GetString(), Is.EqualTo("Ana Dueña"));
            Assert.That(items[0].GetProperty("phone").GetString(), Is.EqualTo("3001112233"));
            Assert.That(items[0].GetProperty("role").GetString(), Is.EqualTo("owner"));
            Assert.That(items[0].GetProperty("is_active").GetBoolean(), Is.True);
            Assert.That(items[1].GetProperty("role").GetString(), Is.EqualTo("seller"));
            Assert.That(items[1].GetProperty("phone").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(text, Does.Not.Contain(stranger.Id.ToString()));
            Assert.That(text, Does.Not.Contain("password").And.Not.Contain("hash").And.Not.Contain("token").And.Not.Contain("stamp"));
        });
    }

    [Test]
    public async Task A_platform_admin_lists_any_company_and_an_unknown_nit_is_404()
    {
        var company = await AddCompanyAsync();
        var owner = await AddUserAsync(company.Id, RoleNames.Owner, "Dueño");
        using var admin = await PlatformAdminAsync();

        using var response = await admin.GetAsync($"/api/v1/companies/{company.Nit}/users");
        var text = await response.Content.ReadAsStringAsync();
        using var missing = await admin.GetAsync($"/api/v1/companies/{MissingNit()}/users");

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
            Assert.That(Items(text).Select(item => item.GetProperty("id").GetGuid()), Is.EqualTo(new[] { owner.Id }));
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(await CodeAsync(missing), Is.EqualTo("user.company_not_found"));
        });
    }

    [Test]
    public async Task A_seller_another_owner_and_a_missing_nit_get_the_same_answer()
    {
        var company = await AddCompanyAsync();
        var other = await AddCompanyAsync();
        var seller = await AddUserAsync(company.Id, RoleNames.Seller, "Vendedor");
        var target = await AddUserAsync(company.Id, RoleNames.Seller, "Otro vendedor");
        var foreignOwner = await AddUserAsync(other.Id, RoleNames.Owner, "Dueño ajeno");
        using var bySeller = await ClientAsync(seller.Email, company.Id);
        using var byForeign = await ClientAsync(foreignOwner.Email, other.Id);
        var missingNit = MissingNit();

        using var sellerOwn = await bySeller.GetAsync($"/api/v1/companies/{company.Nit}/users");
        using var sellerMissing = await bySeller.GetAsync($"/api/v1/companies/{missingNit}/users");
        using var foreignList = await byForeign.GetAsync($"/api/v1/companies/{company.Nit}/users");
        using var foreignMissing = await byForeign.GetAsync($"/api/v1/companies/{missingNit}/users");
        using var foreignPatch = await byForeign.PatchAsJsonAsync($"/api/v1/companies/{company.Nit}/users/{target.Id}", new { is_active = false });
        using var foreignPatchMissing = await byForeign.PatchAsJsonAsync($"/api/v1/companies/{missingNit}/users/{target.Id}", new { is_active = false });

        Assert.Multiple(async () =>
        {
            Assert.That(sellerOwn.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(sellerMissing.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(await CodeAndDetailAsync(sellerOwn), Is.EqualTo(await CodeAndDetailAsync(sellerMissing)));
            Assert.That(foreignList.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(foreignMissing.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(await CodeAndDetailAsync(foreignList), Is.EqualTo(await CodeAndDetailAsync(foreignMissing)).And.StartWith("company.forbidden|"));
            Assert.That(foreignPatch.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(await CodeAndDetailAsync(foreignPatch), Is.EqualTo(await CodeAndDetailAsync(foreignPatchMissing)).And.StartWith("company.forbidden|"));
            Assert.That(await IsActiveAsync(target.Id, company.Id), Is.True, "The foreign owner changed nothing.");
        });
    }

    [Test]
    public async Task The_owner_creates_a_seller_and_an_owner_of_the_company_but_not_of_another()
    {
        var company = await AddCompanyAsync();
        var other = await AddCompanyAsync();
        var owner = await AddUserAsync(company.Id, RoleNames.Owner, "Dueña");
        using var client = await ClientAsync(owner.Email, company.Id);
        var sellerEmail = $"vendedor-{Guid.NewGuid():N}@example.com";
        var ownerEmail = $"socio-{Guid.NewGuid():N}@example.com";

        using var seller = await client.PostAsJsonAsync("/api/v1/users", new { email = sellerEmail, password = Password, name = "Vendedor", role = "seller" });
        using var partner = await client.PostAsJsonAsync(
            "/api/v1/users", new { email = ownerEmail, password = Password, name = "Socio", role = "owner", company_id = company.Id });
        using var foreign = await client.PostAsJsonAsync(
            "/api/v1/users", new { email = $"x-{sellerEmail}", password = Password, name = "X", role = "seller", company_id = other.Id });
        using var platformRole = await client.PostAsJsonAsync(
            "/api/v1/users", new { email = $"y-{sellerEmail}", password = Password, name = "Y", role = "platform_admin" });
        using var list = await client.GetAsync($"/api/v1/companies/{company.Nit}/users");
        var items = Items(await list.Content.ReadAsStringAsync());

        Assert.Multiple(async () =>
        {
            Assert.That(seller.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(partner.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(platformRole.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(await CodeAsync(platformRole), Is.EqualTo("user.role_not_allowed"));
            Assert.That(items.Select(item => (item.GetProperty("email").GetString(), item.GetProperty("role").GetString())),
                Is.EquivalentTo(new (string?, string?)[] { (owner.Email, "owner"), (sellerEmail, "seller"), (ownerEmail, "owner") }));
            Assert.That(await CountAsync("select count(*) from users where email like @email", new { email = $"%-{sellerEmail}" }), Is.Zero);
        });
    }

    [Test]
    public async Task The_owner_changes_a_role_and_deactivates_and_reactivates_a_membership()
    {
        var company = await AddCompanyAsync();
        var owner = await AddUserAsync(company.Id, RoleNames.Owner, "Dueña");
        var seller = await AddUserAsync(company.Id, RoleNames.Seller, "Vendedor", "3005556677");
        using var client = await ClientAsync(owner.Email, company.Id);
        var path = $"/api/v1/companies/{company.Nit}/users/{seller.Id}";

        using var promoted = await client.PatchAsJsonAsync(path, new { role = "owner" });
        var promotedBody = await ReadAsync(promoted);
        using var demoted = await client.PatchAsJsonAsync(path, new { role = "seller" });
        using var deactivated = await client.PatchAsJsonAsync(path, new { is_active = false });
        var deactivatedBody = await ReadAsync(deactivated);
        var listed = Items(await (await client.GetAsync($"/api/v1/companies/{company.Nit}/users")).Content.ReadAsStringAsync())
            .Single(item => item.GetProperty("id").GetGuid() == seller.Id);
        using var reactivated = await client.PatchAsJsonAsync(path, new { is_active = true, role = "seller" });
        var reactivatedBody = await ReadAsync(reactivated);

        Assert.Multiple(async () =>
        {
            Assert.That(promoted.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(promotedBody.GetProperty("id").GetGuid(), Is.EqualTo(seller.Id));
            Assert.That(promotedBody.GetProperty("role").GetString(), Is.EqualTo("owner"));
            Assert.That(promotedBody.GetProperty("phone").GetString(), Is.EqualTo("3005556677"));
            Assert.That(demoted.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(deactivated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(deactivatedBody.GetProperty("is_active").GetBoolean(), Is.False);
            Assert.That(deactivatedBody.GetProperty("role").GetString(), Is.EqualTo("seller"));
            Assert.That(listed.GetProperty("is_active").GetBoolean(), Is.False, "A deactivated member stays in the list.");
            Assert.That(reactivated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(reactivatedBody.GetProperty("is_active").GetBoolean(), Is.True);
            Assert.That(await CountAsync("select count(*) from user_companies where user_id = @id", new { id = seller.Id }), Is.EqualTo(1),
                "A role change keeps a single membership row.");
            Assert.That(await CountAsync("select count(*) from users where id = @id", new { id = seller.Id }), Is.EqualTo(1), "The account is never deleted.");
        });
    }

    [Test]
    public async Task An_invalid_change_answers_422_with_spanish_messages()
    {
        var company = await AddCompanyAsync();
        var owner = await AddUserAsync(company.Id, RoleNames.Owner, "Dueña");
        var seller = await AddUserAsync(company.Id, RoleNames.Seller, "Vendedor");
        using var client = await ClientAsync(owner.Email, company.Id);
        var path = $"/api/v1/companies/{company.Nit}/users/{seller.Id}";

        using var unknownRole = await client.PatchAsJsonAsync(path, new { role = "platform_admin" });
        using var empty = await client.PatchAsJsonAsync(path, new { });
        using var unknownRoleBody = JsonDocument.Parse(await unknownRole.Content.ReadAsStringAsync());
        using var emptyBody = JsonDocument.Parse(await empty.Content.ReadAsStringAsync());

        Assert.Multiple(() =>
        {
            Assert.That(unknownRole.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(unknownRoleBody.RootElement.GetProperty("errors").GetProperty("role")[0].GetString(),
                Is.EqualTo("El rol debe ser «owner» (dueño) o «seller» (vendedor)."));
            Assert.That(empty.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(emptyBody.RootElement.GetProperty("errors").GetProperty("role")[0].GetString(),
                Is.EqualTo("Indique el rol (role), el estado (is_active) o ambos."));
        });
    }

    [Test]
    public async Task A_user_outside_the_company_is_404_like_one_that_does_not_exist()
    {
        var company = await AddCompanyAsync();
        var other = await AddCompanyAsync();
        var owner = await AddUserAsync(company.Id, RoleNames.Owner, "Dueña");
        var outsider = await AddUserAsync(other.Id, RoleNames.Seller, "Ajeno");
        using var client = await ClientAsync(owner.Email, company.Id);

        using var foreign = await client.PatchAsJsonAsync($"/api/v1/companies/{company.Nit}/users/{outsider.Id}", new { is_active = false });
        using var missing = await client.PatchAsJsonAsync($"/api/v1/companies/{company.Nit}/users/{Guid.CreateVersion7()}", new { is_active = false });

        Assert.Multiple(async () =>
        {
            Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(await CodeAndDetailAsync(foreign), Is.EqualTo(await CodeAndDetailAsync(missing)).And.StartWith("user.not_found|"));
            Assert.That(await IsActiveAsync(outsider.Id, other.Id), Is.True);
        });
    }

    [Test]
    public async Task The_owner_cannot_deactivate_or_demote_themselves()
    {
        var company = await AddCompanyAsync();
        var owner = await AddUserAsync(company.Id, RoleNames.Owner, "Dueña");
        await AddUserAsync(company.Id, RoleNames.Owner, "Socio");
        using var client = await ClientAsync(owner.Email, company.Id);
        var path = $"/api/v1/companies/{company.Nit}/users/{owner.Id}";

        using var deactivate = await client.PatchAsJsonAsync(path, new { is_active = false });
        using var demote = await client.PatchAsJsonAsync(path, new { role = "seller" });
        using var same = await client.PatchAsJsonAsync(path, new { role = "owner", is_active = true });

        Assert.Multiple(async () =>
        {
            Assert.That(deactivate.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(await CodeAndDetailAsync(deactivate), Is.EqualTo("user.cannot_deactivate_self|No puede desactivar su propio usuario en la empresa."));
            Assert.That(demote.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(await CodeAndDetailAsync(demote), Is.EqualTo("user.cannot_remove_own_owner_role|No puede quitarse a sí mismo el rol de dueño."));
            Assert.That(same.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Saving the same role and state is allowed.");
            Assert.That(await IsActiveAsync(owner.Id, company.Id), Is.True);
        });
    }

    [Test]
    public async Task The_company_never_loses_its_last_active_owner()
    {
        var company = await AddCompanyAsync();
        var owner = await AddUserAsync(company.Id, RoleNames.Owner, "Única dueña");
        var inactiveOwner = await AddUserAsync(company.Id, RoleNames.Owner, "Dueño inactivo");
        using var admin = await PlatformAdminAsync();
        var path = $"/api/v1/companies/{company.Nit}/users";
        using var first = await admin.PatchAsJsonAsync($"{path}/{inactiveOwner.Id}", new { is_active = false });

        using var deactivate = await admin.PatchAsJsonAsync($"{path}/{owner.Id}", new { is_active = false });
        using var demote = await admin.PatchAsJsonAsync($"{path}/{owner.Id}", new { role = "seller" });
        using var reactivated = await admin.PatchAsJsonAsync($"{path}/{inactiveOwner.Id}", new { is_active = true });
        using var demoteNow = await admin.PatchAsJsonAsync($"{path}/{owner.Id}", new { role = "seller" });

        Assert.Multiple(async () =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK), "With two active owners one can be deactivated.");
            Assert.That(deactivate.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(await CodeAndDetailAsync(deactivate), Is.EqualTo("user.last_owner|La empresa debe conservar al menos un dueño activo."));
            Assert.That(demote.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(await CodeAsync(demote), Is.EqualTo("user.last_owner"));
            Assert.That(reactivated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(demoteNow.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Another active owner remains.");
        });
    }

    [Test]
    public async Task A_deactivated_member_cannot_log_select_or_refresh_into_the_company_and_others_still_can()
    {
        var company = await AddCompanyAsync();
        var other = await AddCompanyAsync();
        var owner = await AddUserAsync(company.Id, RoleNames.Owner, "Dueña");
        var member = await AddUserAsync(company.Id, RoleNames.Seller, "Vendedor de dos empresas");
        await JoinAsync(member.Id, other.Id, RoleNames.Seller);
        var colleague = await AddUserAsync(company.Id, RoleNames.Seller, "Colega");
        var before = await ReadAsync(await LoginAsync(member.Email, company.Id));
        var colleagueSession = await ReadAsync(await LoginAsync(colleague.Email, company.Id));
        using var client = await ClientAsync(owner.Email, company.Id);

        using var deactivated = await client.PatchAsJsonAsync($"/api/v1/companies/{company.Nit}/users/{member.Id}", new { is_active = false });
        var activeTokens = await CountAsync("select count(*) from refresh_tokens where user_id = @id and revoked_at is null", new { id = member.Id });
        using var loginCompany = await LoginAsync(member.Email, company.Id);
        using var loginOther = await LoginAsync(member.Email, other.Id);
        var otherSession = await ReadAsync(loginOther);
        using var selectCompany = await RefreshAsync(otherSession.GetProperty("refresh_token").GetString()!, company.Id);
        using var inOther = _factory.CreateClient(otherSession.GetProperty("access_token").GetString()!);
        using var myCompanies = await inOther.GetAsync("/api/v1/me/companies");
        var companies = Items(await myCompanies.Content.ReadAsStringAsync()).Select(item => item.GetProperty("id").GetGuid()).ToList();
        using var colleagueRefresh = await RefreshAsync(colleagueSession.GetProperty("refresh_token").GetString()!, company.Id);
        // Last: presenting a revoked token is treated as theft and closes every session of the user.
        using var oldRefresh = await RefreshAsync(before.GetProperty("refresh_token").GetString()!, companyId: null);
        var accountActive = await CountAsync("select count(*) from users where id = @id and is_active = @active", new { id = member.Id, active = true });

        Assert.Multiple(async () =>
        {
            Assert.That(deactivated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(loginCompany.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(await CodeAsync(loginCompany), Is.EqualTo("identity.company_not_allowed"));
            Assert.That(loginOther.StatusCode, Is.EqualTo(HttpStatusCode.OK), "The other membership is untouched.");
            Assert.That(otherSession.GetProperty("roles").EnumerateArray().Select(role => role.GetString()), Is.EqualTo(new[] { "seller" }));
            Assert.That(oldRefresh.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "The sessions open before the change were revoked.");
            Assert.That(selectCompany.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "Selecting the company is refused.");
            Assert.That(companies, Is.EqualTo(new[] { other.Id }), "The selector no longer offers the company.");
            Assert.That(colleagueRefresh.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Other members keep their sessions.");
            Assert.That(activeTokens, Is.Zero, "Deactivating revoked every refresh token of the user.");
            Assert.That(accountActive, Is.EqualTo(1), "The account stays active: only the membership was deactivated.");
            Assert.That(await IsActiveAsync(member.Id, other.Id), Is.True);
        });

        using var reactivated = await client.PatchAsJsonAsync($"/api/v1/companies/{company.Nit}/users/{member.Id}", new { is_active = true });
        using var loginAgain = await LoginAsync(member.Email, company.Id);
        Assert.Multiple(() =>
        {
            Assert.That(reactivated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(loginAgain.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Reactivating restores access.");
        });
    }

    [Test]
    public async Task A_deactivated_owner_loses_the_owner_operations_at_once()
    {
        var company = await AddCompanyAsync();
        var owner = await AddUserAsync(company.Id, RoleNames.Owner, "Dueña");
        var partner = await AddUserAsync(company.Id, RoleNames.Owner, "Socio");
        using var byOwner = await ClientAsync(owner.Email, company.Id);
        using var byPartner = await ClientAsync(partner.Email, company.Id);

        using var deactivated = await byOwner.PatchAsJsonAsync($"/api/v1/companies/{company.Nit}/users/{partner.Id}", new { is_active = false });
        using var list = await byPartner.GetAsync($"/api/v1/companies/{company.Nit}/users");
        using var create = await byPartner.PostAsJsonAsync(
            "/api/v1/users", new { email = $"z-{Guid.NewGuid():N}@example.com", password = Password, name = "Z", role = "owner" });

        Assert.Multiple(async () =>
        {
            Assert.That(deactivated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "Its access token is still valid, but the membership is checked in the database.");
            Assert.That(await CodeAsync(list), Is.EqualTo("company.forbidden"));
            Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        });
    }

    private static string MissingNit() => Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture) + "9";

    private static async Task<(Guid Id, string Nit)> AddCompanyAsync()
    {
        var id = await IdentityDatabase.AddCompanyAsync();
        await using var connection = TestDatabase.Connect(IdentityDatabase.ConnectionString);
        return (id, await connection.QuerySingleAsync<string>("select identification_number from companies where id = @id", new { id }));
    }

    private static async Task<(Guid Id, string Email)> AddUserAsync(
        Guid companyId, string role, string name, string? phone = null, string? email = null)
    {
        email ??= $"user-{Guid.NewGuid():N}@example.com";
        var id = await IdentityStoreTests.AddUserAsync([], new CompanyMembership(companyId, role), email, Password);
        await using var connection = TestDatabase.Connect(IdentityDatabase.ConnectionString);
        await connection.ExecuteAsync("update users set name = @name, phone = @phone where id = @id", new { id, name, phone });
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

    private static async Task<bool> IsActiveAsync(Guid userId, Guid companyId)
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

    private static async Task<string> CodeAndDetailAsync(HttpResponseMessage response)
    {
        var body = await ReadAsync(response);
        var code = body.TryGetProperty("code", out var value) ? value.GetString() : null;
        var detail = body.TryGetProperty("detail", out var text) ? text.GetString() : null;
        return $"{code}|{detail}";
    }
}
