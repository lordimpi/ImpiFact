using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Identity;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Seeding;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ImpiFact.Integration.Tests.IdentityData;

/// <summary>
/// <c>GET /api/v1/me/companies</c> contra PostgreSQL: las empresas a las que puede entrar quien llama, con razón social,
/// NIT (número de identificación tributaria), dígito de verificación, roles y estado, ordenadas por nombre. Nunca
/// aparece una empresa de la que la credencial no es parte.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class MyCompaniesTests
{
    private const string Password = "Contraseña-Segura-1";

    private IdentityDataApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi() => _factory = new IdentityDataApiFactory();

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    [Test]
    public async Task A_user_sees_every_company_with_its_name_nit_roles_and_state_ordered_by_name_and_no_other()
    {
        var zeta = await AddCompanyAsync("Zeta Comercial S.A.S.", isActive: true);
        var avila = await AddCompanyAsync("Ávila Distribuciones", isActive: false);
        var foreign = await AddCompanyAsync("Ajena Ltda.", isActive: true);
        var userId = await IdentityStoreTests.AddUserAsync([], new CompanyMembership(zeta.Id, RoleNames.Owner), Email(), Password);
        await JoinAsync(userId, avila.Id, RoleNames.Seller);
        await JoinAsync(userId, zeta.Id, RoleNames.Seller);
        await IdentityStoreTests.AddUserAsync([], new CompanyMembership(foreign.Id, RoleNames.Owner), Email(), Password);
        var email = await EmailOfAsync(userId);

        using var withoutCompany = _factory.CreateClient(await AccessTokenAsync(email, companyId: null));
        using var withCompany = _factory.CreateClient(await AccessTokenAsync(email, zeta.Id));
        using var response = await withoutCompany.GetAsync("/api/v1/me/companies");
        var body = await ReadAsync(response);
        var fromActiveCompany = await ReadAsync(await withCompany.GetAsync("/api/v1/me/companies"));
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(items.Select(item => item.GetProperty("id").GetGuid()), Is.EqualTo(new[] { avila.Id, zeta.Id }), "Ordered by name, accents included.");
            Assert.That(items[0].GetProperty("name").GetString(), Is.EqualTo("Ávila Distribuciones"));
            Assert.That(items[0].GetProperty("nit").GetString(), Is.EqualTo(avila.Nit));
            Assert.That(items[0].GetProperty("dv").GetString(), Is.EqualTo("1"));
            Assert.That(items[0].GetProperty("is_active").GetBoolean(), Is.False);
            Assert.That(Roles(items[0]), Is.EqualTo(new[] { "seller" }));
            Assert.That(items[1].GetProperty("is_active").GetBoolean(), Is.True);
            Assert.That(Roles(items[1]), Is.EqualTo(new[] { "owner", "seller" }));
            Assert.That(body.GetRawText(), Does.Not.Contain(foreign.Id.ToString()));
            Assert.That(fromActiveCompany.GetRawText(), Is.EqualTo(body.GetRawText()), "The active company does not narrow the list.");
        });
    }

    [Test]
    public async Task A_platform_admin_without_memberships_gets_an_empty_list()
    {
        await AddCompanyAsync("Otra Empresa S.A.S.", isActive: true);
        var userId = await IdentityStoreTests.AddUserAsync([RoleNames.PlatformAdmin], company: null, Email(), Password);
        using var admin = _factory.CreateClient(await AccessTokenAsync(await EmailOfAsync(userId), companyId: null));

        using var response = await admin.GetAsync("/api/v1/me/companies");
        var body = await ReadAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.GetProperty("items").GetArrayLength(), Is.Zero);
        });
    }

    [Test]
    public async Task A_company_api_key_gets_only_its_own_company_without_roles()
    {
        var own = await AddCompanyAsync("Llave Propia S.A.S.", isActive: true);
        var userId = await IdentityStoreTests.AddUserAsync([], new CompanyMembership(own.Id, RoleNames.Owner), Email(), Password);
        await AddCompanyAsync("Vecina S.A.S.", isActive: true);
        using var owner = _factory.CreateClient(await AccessTokenAsync(await EmailOfAsync(userId), own.Id));
        var key = await ReadAsync(await owner.PostAsJsonAsync("/api/v1/api-keys", new { name = "ERP", scopes = new[] { ApiScopes.DocumentsRead } }));
        using var withKey = _factory.CreateClient(key.GetProperty("key").GetString()!);

        using var response = await withKey.GetAsync("/api/v1/me/companies");
        var items = (await ReadAsync(response)).GetProperty("items").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(items.Select(item => item.GetProperty("id").GetGuid()), Is.EqualTo(new[] { own.Id }));
            Assert.That(items[0].GetProperty("name").GetString(), Is.EqualTo("Llave Propia S.A.S."));
            Assert.That(Roles(items[0]), Is.Empty);
        });
    }

    [Test]
    public async Task Without_credentials_the_list_answers_401()
    {
        using var response = await _factory.CreateClient().GetAsync("/api/v1/me/companies");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private static string Email() => $"user-{Guid.NewGuid():N}@example.com";

    private static IEnumerable<string?> Roles(JsonElement item) => item.GetProperty("roles").EnumerateArray().Select(role => role.GetString());

    private static async Task<(Guid Id, string Nit)> AddCompanyAsync(string name, bool isActive)
    {
        var id = await IdentityDatabase.AddCompanyAsync();
        await using var connection = TestDatabase.Connect(IdentityDatabase.ConnectionString);
        // MySQL no tiene RETURNING: el UPDATE y el SELECT van en el mismo comando.
        var nit = await connection.QuerySingleAsync<string>(
            TestDatabase.IsSqlServer
                ? "update companies set name = @name, is_active = @isActive output inserted.identification_number where id = @id"
                : TestDatabase.IsMySql
                    ? "update companies set name = @name, is_active = @isActive where id = @id; select identification_number from companies where id = @id"
                    : "update companies set name = @name, is_active = @isActive where id = @id returning identification_number",
            new { id, name, isActive });
        return (id, nit);
    }

    private static async Task JoinAsync(Guid userId, Guid companyId, string role)
    {
        await using var scope = IdentityDatabase.Open(companyId);
        var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
        db.Add(new UserCompany { UserId = userId, CompanyId = companyId, RoleId = PlatformSeed.Roles.Single(candidate => candidate.Name == role).Id });
        await db.SaveChangesAsync();
    }

    private static async Task<string> EmailOfAsync(Guid userId)
    {
        await using var connection = TestDatabase.Connect(IdentityDatabase.ConnectionString);
        return await connection.QuerySingleAsync<string>("select email from users where id = @userId", new { userId });
    }

    private async Task<string> AccessTokenAsync(string email, Guid? companyId)
    {
        using var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/login", new { email, password = Password, company_id = companyId });
        return (await ReadAsync(response)).GetProperty("access_token").GetString()!;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text.Length == 0 ? "{}" : text);
        return document.RootElement.Clone();
    }
}
