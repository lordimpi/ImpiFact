using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Identity;
using ImpiFact.Domain.Masters;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Integration.Tests.Protection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Companies;

/// <summary>Alta, actualización y configuración de la empresa de punta a punta, por la API y con PostgreSQL.</summary>
[Category("Integration")]
public sealed class CompanyApiTests
{
    private static CompanyApiFactory Api => CompaniesHost.Api;

    [Test]
    public async Task Platform_creates_a_company_with_the_php_defaults_and_its_api_key_works()
    {
        var (nit, dv) = CompaniesHost.NewNit();
        using var platform = await PlatformClientAsync();

        using var response = await platform.PostAsJsonAsync($"/api/ubl2.1/config/{nit}/{dv}", CompanyBody(nit));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), ServerErrors);
        using var json = await Json(response);
        var root = json.RootElement;
        var token = root.GetProperty("token").GetString()!;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("message").GetString(), Is.EqualTo("Empresa creada/actualizada con éxito"));
            Assert.That(root.GetProperty("created").GetBoolean(), Is.True);
            Assert.That(token, Does.StartWith("apk_"));
            Assert.That(root.GetProperty("company").GetProperty("identification_number").GetString(), Is.EqualTo(nit));
        });

        await using (var scope = Api.OpenScope(null, null, ActorType.System))
        {
            var company = await scope.Db.Set<Company>().SingleAsync(row => row.IdentificationNumber == nit);
            var environments = await Rows<CompanyEnvironment>(scope.Db, company.Id).ToListAsync();
            var liabilities = await Rows<CompanyLiability>(scope.Db, company.Id).Select(row => row.TypeLiabilityId).ToListAsync();
            var customer = await Rows<Customer>(scope.Db, company.Id).SingleAsync();
            var key = await scope.Db.Set<ApiKey>().SingleAsync(row => row.CompanyId == company.Id);
            Assert.Multiple(() =>
            {
                Assert.That((company.LanguageId, company.CountryId, company.TypeCurrencyId, company.TypeOperationId), Is.EqualTo((79, 46, 35, 10)));
                Assert.That(company.TaxId, Is.EqualTo(15), "Regime 2 defaults the tax to 15.");
                Assert.That(company.Phone, Is.EqualTo("3216547"), "The legacy contract sends the phone as a number.");
                Assert.That(environments.Select(row => row.DocumentKind), Is.EquivalentTo(Enum.GetValues<DocumentKind>()));
                Assert.That(environments.Select(row => row.TypeEnvironmentId), Is.All.EqualTo(2));
                Assert.That(liabilities, Is.EquivalentTo(new[] { 7, 14 }));
                Assert.That((customer.IdentificationNumber, customer.Name, customer.TypeDocumentIdentificationId), Is.EqualTo(("2222222222", "Cliente por Defecto", 3)));
                Assert.That(key.KeyHash, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)))));
                Assert.That(key.KeyHash, Does.Not.Contain(token));
            });
        }

        using var company2 = Api.ClientWith(token);
        using var listing = await company2.GetAsync($"/api/ubl2.1/certificates-listing/{nit}");
        Assert.That(listing.StatusCode, Is.EqualTo(HttpStatusCode.OK), "The returned key belongs to the new company.");
    }

    [Test]
    public async Task Same_nit_again_updates_the_company_instead_of_creating_another()
    {
        var (nit, dv) = CompaniesHost.NewNit();
        using var platform = await PlatformClientAsync();
        (await platform.PostAsJsonAsync($"/api/ubl2.1/config/{nit}/{dv}", CompanyBody(nit))).Dispose();

        using var again = await platform.PostAsJsonAsync($"/api/v1/companies/{nit}/{dv}", CompanyBody(nit, businessName: "NUEVO NOMBRE"));

        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var json = await Json(again);
        Assert.That(json.RootElement.GetProperty("created").GetBoolean(), Is.False);
        Assert.That(json.RootElement.GetProperty("token").ValueKind, Is.EqualTo(JsonValueKind.Null));
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var company = await scope.Db.Set<Company>().SingleAsync(row => row.IdentificationNumber == nit);
        Assert.Multiple(async () =>
        {
            Assert.That(company.Name, Is.EqualTo("NUEVO NOMBRE"));
            Assert.That(await Rows<Customer>(scope.Db, company.Id).CountAsync(), Is.EqualTo(1));
            Assert.That(await scope.Db.Set<ApiKey>().CountAsync(row => row.CompanyId == company.Id), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Email_of_another_company_is_rejected_with_the_php_message()
    {
        var (first, firstDv) = CompaniesHost.NewNit();
        var (second, secondDv) = CompaniesHost.NewNit();
        using var platform = await PlatformClientAsync();
        (await platform.PostAsJsonAsync($"/api/ubl2.1/config/{first}/{firstDv}", CompanyBody(first))).Dispose();

        using var response = await platform.PostAsJsonAsync($"/api/ubl2.1/config/{second}/{secondDv}", CompanyBody(second, email: $"{first}@example.com"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        using var json = await Json(response);
        Assert.That(json.RootElement.GetProperty("message").GetString(),
            Is.EqualTo("Error de registro, el correo electronico ya existe o es el mismo que tiene registrado actualmente."));
        Assert.That(json.RootElement.GetProperty("success").GetBoolean(), Is.False);
    }

    [Test]
    public async Task Invalid_data_answers_422_with_the_php_messages()
    {
        var (nit, _) = CompaniesHost.NewNit();
        using var platform = await PlatformClientAsync();
        var body = CompanyBody(nit);
        body.Remove("business_name");
        body["municipality_id"] = 999999;

        using var response = await platform.PostAsJsonAsync($"/api/ubl2.1/config/{nit}/{(Application.Features.Companies.Nit.CheckDigit(nit) + 1) % 10}", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        using var json = await Json(response);
        var errors = json.RootElement.GetProperty("errors");
        Assert.Multiple(() =>
        {
            Assert.That(errors.GetProperty("business_name")[0].GetString(), Is.EqualTo("El campo business name es obligatorio."));
            Assert.That(errors.GetProperty("dv")[0].GetString(), Is.EqualTo("Error, el digito de verificacion no es valido para este NIT."));
            Assert.That(errors.GetProperty("municipality_id")[0].GetString(), Is.EqualTo("municipality id es inválido."));
        });
    }

    [Test]
    public async Task Company_key_cannot_create_companies_on_the_platform_route()
    {
        var (company, _) = await CreateCompanyAsync();
        var (nit, dv) = CompaniesHost.NewNit();

        using var response = await company.PostAsJsonAsync($"/api/v1/companies/{nit}/{dv}", CompanyBody(nit));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        Assert.That(await scope.Db.Set<Company>().AnyAsync(row => row.IdentificationNumber == nit), Is.False);
    }

    [Test]
    public async Task Company_updates_itself_through_the_legacy_route()
    {
        var (company, nit) = await CreateCompanyAsync();

        using var response = await company.PostAsJsonAsync(
            $"/api/ubl2.1/config/{nit}/{Application.Features.Companies.Nit.CheckDigit(nit)}", CompanyBody(nit, businessName: "MI EMPRESA"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        using var json = await Json(response);
        Assert.That(json.RootElement.GetProperty("created").GetBoolean(), Is.False);
        Assert.That(json.RootElement.GetProperty("company").GetProperty("business_name").GetString(), Is.EqualTo("MI EMPRESA"));
    }

    [Test]
    public async Task Nit_of_another_company_is_forbidden_without_revealing_whether_it_exists()
    {
        var (company, _) = await CreateCompanyAsync();
        var (_, otherNit) = await CreateCompanyAsync();
        var (missingNit, missingDv) = CompaniesHost.NewNit();
        var otherDv = Application.Features.Companies.Nit.CheckDigit(otherNit);

        using var existing = await company.PutAsJsonAsync($"/api/v1/companies/{otherNit}/environment", new { factura_ventas = 1 });
        using var missing = await company.PutAsJsonAsync($"/api/v1/companies/{missingNit}/environment", new { factura_ventas = 1 });
        using var legacyExisting = await company.PostAsJsonAsync($"/api/ubl2.1/config/{otherNit}/{otherDv}", CompanyBody(otherNit));
        using var legacyMissing = await company.PostAsJsonAsync($"/api/ubl2.1/config/{missingNit}/{missingDv}", CompanyBody(missingNit));
        using var listing = await company.GetAsync($"/api/ubl2.1/certificates-listing/{otherNit}");

        Assert.Multiple(async () =>
        {
            foreach (var response in new[] { existing, missing, legacyExisting, legacyMissing, listing })
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), response.RequestMessage!.RequestUri!.ToString());
            }

            Assert.That(await Detail(existing), Is.EqualTo(await Detail(missing)));
            Assert.That(await Detail(legacyExisting), Is.EqualTo(await Detail(legacyMissing)));
        });
    }

    [Test]
    public async Task Software_pin_is_stored_only_encrypted_and_never_returned()
    {
        var (company, nit) = await CreateCompanyAsync();

        using var response = await company.PutAsJsonAsync("/api/ubl2.1/config/software", new { id = "soft-123", pin = 12345, test_set_id = "set-1" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Does.Not.Contain("12345"));
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var software = await Rows<CompanySoftware>(scope.Db, await CompanyIdAsync(scope.Db, nit)).SingleAsync();
        var secrets = scope.Services.GetRequiredService<ISecretProtector>();
        Assert.Multiple(() =>
        {
            Assert.That(software.PinCipher, Does.Not.Contain("12345"));
            Assert.That(secrets.Unprotect(SecretPurposes.SoftwarePin, software.PinCipher), Is.EqualTo("12345"));
            Assert.That((software.DocumentKind, software.TypeEnvironmentId), Is.EqualTo((DocumentKind.Invoice, 2)));
            Assert.That(software.DianUrl, Is.EqualTo("https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc"));
            Assert.That(software.TestSetId, Is.EqualTo("set-1"));
        });
    }

    [Test]
    public async Task Software_pin_must_have_5_digits()
    {
        var (company, _) = await CreateCompanyAsync();

        using var response = await company.PutAsJsonAsync("/api/ubl2.1/config/software", new { id = "soft-123", pin = "1234" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        using var json = await Json(response);
        Assert.That(json.RootElement.GetProperty("errors").GetProperty("pin")[0].GetString(), Is.EqualTo("pin debe tener 5 dígitos."));
    }

    [Test]
    public async Task Environment_needs_software_first_and_then_moves_it_to_production()
    {
        var (company, nit) = await CreateCompanyAsync();

        using var withoutSoftware = await company.PutAsJsonAsync("/api/ubl2.1/config/environment", new { factura_ventas = 1 });
        (await company.PutAsJsonAsync("/api/ubl2.1/config/software", new { id = "soft-123", pin = "12345" })).Dispose();
        using var withSoftware = await company.PutAsJsonAsync($"/api/v1/companies/{nit}/environment", new { factura_ventas = 1 });

        Assert.That(withoutSoftware.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        using (var json = await Json(withoutSoftware))
        {
            Assert.That(json.RootElement.GetProperty("message").GetString(),
                Is.EqualTo("No se ha configurado el software para esta empresa. Configure el software primero."));
        }

        Assert.That(withSoftware.StatusCode, Is.EqualTo(HttpStatusCode.OK), await withSoftware.Content.ReadAsStringAsync());
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var companyId = await CompanyIdAsync(scope.Db, nit);
        var invoice = await Rows<CompanyEnvironment>(scope.Db, companyId).SingleAsync(row => row.DocumentKind == DocumentKind.Invoice);
        var production = await Rows<CompanySoftware>(scope.Db, companyId).SingleAsync(row => row.TypeEnvironmentId == 1);
        Assert.That(invoice.TypeEnvironmentId, Is.EqualTo(1));
        Assert.That(production.DianUrl, Is.EqualTo("https://vpfe.dian.gov.co/WcfDianCustomerServices.svc"));
    }

    [Test]
    public async Task Resolution_is_upserted_and_its_prefix_has_at_most_4_characters()
    {
        var (company, nit) = await CreateCompanyAsync();
        var resolution = new Dictionary<string, object>
        {
            ["type_document_id"] = 1, ["prefix"] = "SETP", ["resolution"] = "18760000001", ["resolution_date"] = "2019-01-19",
            ["technical_key"] = "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c", ["from"] = 990000000, ["to"] = 995000000,
            ["date_from"] = "2019-01-19", ["date_to"] = "2030-01-19",
        };

        using var first = await company.PutAsJsonAsync("/api/ubl2.1/config/resolution", resolution);
        resolution["to"] = 996000000;
        using var second = await company.PutAsJsonAsync($"/api/v1/companies/{nit}/resolution", resolution);
        resolution["prefix"] = "SETPX";
        using var tooLong = await company.PutAsJsonAsync("/api/ubl2.1/config/resolution", resolution);

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK), await first.Content.ReadAsStringAsync());
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(tooLong.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        using (var json = await Json(tooLong))
        {
            Assert.That(json.RootElement.GetProperty("errors").GetProperty("prefix")[0].GetString(), Is.EqualTo("prefix no debe ser mayor que 4 caracteres."));
        }

        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var rows = await Rows<Resolution>(scope.Db, await CompanyIdAsync(scope.Db, nit)).ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That((rows[0].RangeTo, rows[0].TypeEnvironmentId, rows[0].IsActive), Is.EqualTo((996000000L, 2, true)));
    }

    [Test]
    public async Task Certificate_with_a_wrong_password_or_expired_is_rejected_and_nothing_is_stored()
    {
        var (company, nit) = await CreateCompanyAsync();
        var valid = TestPfx.Rsa();
        var expired = TestPfx.Rsa(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

        using var wrongPassword = await company.PutAsJsonAsync("/api/ubl2.1/config/certificate",
            new { certificate = Convert.ToBase64String(valid), password = "otra" });
        using var expiredResponse = await company.PutAsJsonAsync("/api/ubl2.1/config/certificate",
            new { certificate = Convert.ToBase64String(expired), password = TestPfx.Password });
        using var notBase64 = await company.PutAsJsonAsync("/api/ubl2.1/config/certificate",
            new { certificate = "no es base64", password = TestPfx.Password });

        Assert.Multiple(async () =>
        {
            Assert.That(wrongPassword.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(await Code(wrongPassword), Is.EqualTo("certificate.invalid"));
            Assert.That(expiredResponse.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(await Code(expiredResponse), Is.EqualTo("certificate.expired"));
            Assert.That(notBase64.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(await notBase64.Content.ReadAsStringAsync(), Does.Not.Contain(TestPfx.Password));
        });
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        Assert.That(await Rows<Certificate>(scope.Db, await CompanyIdAsync(scope.Db, nit)).AnyAsync(), Is.False);
    }

    [Test]
    public async Task Api_key_uploads_a_valid_certificate_without_an_uploading_user()
    {
        var (company, nit) = await CreateCompanyAsync();

        using var response = await company.PutAsJsonAsync("/api/ubl2.1/config/certificate",
            new { certificate = Convert.ToBase64String(TestPfx.Rsa()), password = TestPfx.Password });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var row = await Rows<Certificate>(scope.Db, await CompanyIdAsync(scope.Db, nit)).SingleAsync();
        Assert.That(row.UploadedBy, Is.Null);
    }

    [Test]
    public async Task Logs_never_carry_the_api_key_pin_or_certificate_password()
    {
        var (nit, dv) = CompaniesHost.NewNit();
        using var platform = await PlatformClientAsync();
        using var created = await platform.PostAsJsonAsync($"/api/ubl2.1/config/{nit}/{dv}", CompanyBody(nit));
        using var createdJson = await Json(created);
        var token = createdJson.RootElement.GetProperty("token").GetString()!;
        using var company = Api.ClientWith(token);
        const string pin = "86420";
        const string password = "clave-del-certificado-77";

        (await company.PutAsJsonAsync("/api/ubl2.1/config/software", new { id = "soft-log", pin })).Dispose();
        (await company.PutAsJsonAsync("/api/ubl2.1/config/software", new { id = "soft-log", pin = "9" })).Dispose();
        (await company.PutAsJsonAsync("/api/ubl2.1/config/certificate",
            new { certificate = Convert.ToBase64String(TestPfx.Rsa(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero), password)), password })).Dispose();

        var logs = string.Join(Environment.NewLine, Api.Logs.Entries);
        Assert.That(logs, Does.Not.Contain(token).And.Not.Contain(pin).And.Not.Contain(password));
    }

    /// <summary>Errores que registró la API, para explicar un 500 en el mensaje de la prueba.</summary>
    internal static string ServerErrors() =>
        string.Join(Environment.NewLine, Api.Logs.Entries.Where(entry => entry.StartsWith("Error", StringComparison.Ordinal)).TakeLast(3));

    private static async Task<HttpClient> PlatformClientAsync() => await Api.ClientWithNewKeyAsync(null, ApiScopes.Platform);

    /// <summary>Crea una empresa por la plataforma y devuelve un cliente con la API key que entregó el alta.</summary>
    internal static async Task<(HttpClient Client, string Nit)> CreateCompanyAsync()
    {
        var (nit, dv) = CompaniesHost.NewNit();
        using var platform = await PlatformClientAsync();
        using var response = await platform.PostAsJsonAsync($"/api/ubl2.1/config/{nit}/{dv}", CompanyBody(nit));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), ServerErrors);
        using var json = await Json(response);
        return (Api.ClientWith(json.RootElement.GetProperty("token").GetString()!), nit);
    }

    /// <summary>Cuerpo del alta con la forma del ejemplo del contrato heredado (teléfono como número).</summary>
    internal static Dictionary<string, object> CompanyBody(string nit, string businessName = "EMPRESA DE PRUEBAS", string? email = null) => new()
    {
        ["type_document_identification_id"] = 6,
        ["type_organization_id"] = 1,
        ["type_regime_id"] = 2,
        ["type_liability_id"] = 14,
        ["type_liability_ids"] = new[] { 7, 14 },
        ["business_name"] = businessName,
        ["merchant_registration"] = "1234567-12",
        ["municipality_id"] = 1006,
        ["address"] = "CALLE 1 1C 1",
        ["phone"] = 3216547,
        ["email"] = email ?? $"{nit}@example.com",
    };

    internal static async Task<Guid> CompanyIdAsync(ImpiFactDbContext db, string nit) =>
        (await db.Set<Company>().SingleAsync(row => row.IdentificationNumber == nit)).Id;

    internal static IQueryable<T> Rows<T>(ImpiFactDbContext db, Guid companyId)
        where T : class, ITenantOwned =>
        db.Set<T>().IgnoreQueryFilters().Where(row => row.CompanyId == companyId);

    internal static async Task<JsonDocument> Json(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        using var json = await Json(response);
        return json.RootElement.GetProperty("code").GetString();
    }

    private static async Task<string> Detail(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("code").GetString() + "|" + json.RootElement.GetProperty("detail").GetString();
    }
}
