using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Domain.Companies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Companies;

/// <summary>
/// Own mail server of the company through the API (<c>GET</c>, <c>PUT</c> and <c>DELETE
/// /api/v1/companies/{nit}/mail-settings</c>), stored in <c>company_mail_settings</c>: the password is write-only, stored
/// encrypted with the SMTP purpose of the secret protector and never returned; only the company that owns the NIT reads
/// or changes it.
/// </summary>
[Category("Integration")]
public sealed class CompanyMailSettingsApiTests
{
    private const string Password = "Smtp-Secret-2026!";

    private const string ServerChangedMessage =
        "El campo contraseña es obligatorio al cambiar el servidor, el puerto, el usuario o el cifrado del correo.";

    private static CompanyApiFactory Api => CompaniesHost.Api;

    internal static object Body(
        string? password = Password, string host = "smtp.empresa.test", int port = 587, string encryption = "tls",
        string username = "facturacion@empresa.test", string fromName = "Facturación Empresa", bool usePlatformDefault = false) => new
    {
        host,
        port,
        username,
        password,
        encryption,
        from_address = "facturacion@empresa.test",
        from_name = fromName,
        use_platform_default = usePlatformDefault,
    };

    [Test]
    public async Task The_saved_server_is_read_back_without_its_password()
    {
        var (company, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var owned = company;

        using var saved = await company.PutAsJsonAsync($"/api/v1/companies/{nit}/mail-settings", Body());
        using var fetched = await company.GetAsync($"/api/v1/companies/{nit}/mail-settings");

        var savedText = await saved.Content.ReadAsStringAsync();
        var fetchedText = await fetched.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(fetchedText);
        var root = json.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK), savedText);
            Assert.That(fetched.StatusCode, Is.EqualTo(HttpStatusCode.OK), fetchedText);
            Assert.That(root.GetProperty("in_use").GetString(), Is.EqualTo("company"));
            Assert.That(root.GetProperty("use_platform_default").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("host").GetString(), Is.EqualTo("smtp.empresa.test"));
            Assert.That(root.GetProperty("port").GetInt32(), Is.EqualTo(587));
            Assert.That(root.GetProperty("username").GetString(), Is.EqualTo("facturacion@empresa.test"));
            Assert.That(root.GetProperty("encryption").GetString(), Is.EqualTo("tls"));
            Assert.That(root.GetProperty("from_address").GetString(), Is.EqualTo("facturacion@empresa.test"));
            Assert.That(root.GetProperty("from_name").GetString(), Is.EqualTo("Facturación Empresa"));
            Assert.That(root.GetProperty("has_password").GetBoolean(), Is.True);
            Assert.That(root.TryGetProperty("password", out _), Is.False, "The password is write-only.");
            Assert.That(savedText + fetchedText, Does.Not.Contain(Password));
            Assert.That(Api.Logs.Entries, Has.None.Contains(Password), "The password never reaches the logs.");
        });
    }

    [Test]
    public async Task The_password_is_stored_encrypted_for_the_smtp_purpose()
    {
        var (company, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var owned = company;

        using var saved = await company.PutAsJsonAsync($"/api/v1/companies/{nit}/mail-settings", Body());

        var row = await RowAsync(nit);
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var secrets = scope.Services.GetRequiredService<ISecretProtector>();
        Assert.Multiple(() =>
        {
            Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(row!.PasswordCipher, Is.Not.Null.And.Not.Contains(Password));
            Assert.That(secrets.Unprotect(SecretPurposes.SmtpPassword, row.PasswordCipher!), Is.EqualTo(Password));
            Assert.That(() => secrets.Unprotect(SecretPurposes.SoftwarePin, row.PasswordCipher!), Throws.InstanceOf<System.Security.Cryptography.CryptographicException>());
        });
    }

    [Test]
    public async Task A_new_save_without_password_keeps_the_stored_one_and_the_first_save_requires_it()
    {
        var (company, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var owned = company;

        using var first = await company.PutAsJsonAsync($"/api/v1/companies/{nit}/mail-settings", Body(password: null));
        using var saved = await company.PutAsJsonAsync($"/api/v1/companies/{nit}/mail-settings", Body());
        var cipher = (await RowAsync(nit))!.PasswordCipher;
        using var changed = await company.PutAsJsonAsync(
            $"/api/v1/companies/{nit}/mail-settings", Body(password: null, fromName: "Otra firma", usePlatformDefault: true));

        var row = await RowAsync(nit);
        using var firstBody = await CompanyApiTests.Json(first);
        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(firstBody.RootElement.GetProperty("errors").GetProperty("password")[0].GetString(),
                Is.EqualTo("El campo contraseña es obligatorio para guardar el servidor de correo por primera vez."));
            Assert.That(changed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((row!.FromName, row.UsePlatformDefault), Is.EqualTo(("Otra firma", true)));
            Assert.That(row.PasswordCipher, Is.EqualTo(cipher), "Without a new password, on the same server, the stored one stays.");
        });
    }

    // End-of-7a verification, M2: keeping the stored password while the server changed let a config:write credential point
    // the Worker to its own host and receive the company's SMTP password at login.
    [Test]
    public async Task Changing_the_server_without_a_new_password_answers_422_and_keeps_the_old_server()
    {
        var (company, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var owned = company;
        using var saved = await company.PutAsJsonAsync($"/api/v1/companies/{nit}/mail-settings", Body());
        var before = await RowAsync(nit);

        using var moved = await company.PutAsJsonAsync($"/api/v1/companies/{nit}/mail-settings", Body(password: null, host: "smtp.attacker.test"));

        var after = await RowAsync(nit);
        using var body = await CompanyApiTests.Json(moved);
        Assert.Multiple(() =>
        {
            Assert.That(moved.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(body.RootElement.GetProperty("errors").GetProperty("password")[0].GetString(), Is.EqualTo(ServerChangedMessage));
            Assert.That(after!.Host, Is.EqualTo(before!.Host));
            Assert.That(after.PasswordCipher, Is.EqualTo(before.PasswordCipher));
        });
    }

    [TestCase("port")]
    [TestCase("username")]
    [TestCase("encryption")]
    public async Task Changing_the_port_user_or_encryption_needs_a_new_password(string field)
    {
        var (company, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var owned = company;
        using var saved = await company.PutAsJsonAsync($"/api/v1/companies/{nit}/mail-settings", Body());
        var before = await RowAsync(nit);
        var changed = field switch
        {
            "port" => Body(password: null, port: 2525),
            "username" => Body(password: null, username: "otro@attacker.test"),
            _ => Body(password: null, encryption: "none"),
        };

        using var moved = await company.PutAsJsonAsync($"/api/v1/companies/{nit}/mail-settings", changed);
        using var withPassword = await company.PutAsJsonAsync($"/api/v1/companies/{nit}/mail-settings", field switch
        {
            "port" => Body(port: 2525),
            "username" => Body(username: "otro@attacker.test"),
            _ => Body(encryption: "none"),
        });

        var after = await RowAsync(nit);
        using var body = await CompanyApiTests.Json(moved);
        Assert.Multiple(() =>
        {
            Assert.That(moved.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(body.RootElement.GetProperty("errors").GetProperty("password")[0].GetString(), Is.EqualTo(ServerChangedMessage));
            Assert.That(withPassword.StatusCode, Is.EqualTo(HttpStatusCode.OK), "With a new password the change is saved.");
            Assert.That((after!.Port, after.Username, after.Encryption), Is.Not.EqualTo((before!.Port, before.Username, before.Encryption)));
        });
    }

    [Test]
    public async Task Invalid_fields_answer_422_with_a_spanish_message_per_field()
    {
        var (company, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var owned = company;

        using var response = await company.PutAsJsonAsync($"/api/v1/companies/{nit}/mail-settings", new
        {
            host = "smtp://mal host",
            port = 70000,
            username = "",
            password = Password,
            encryption = "rot13",
            from_address = "no-es-correo",
        });

        using var json = await CompanyApiTests.Json(response);
        var errors = json.RootElement.GetProperty("errors");
        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), json.RootElement.ToString());
            Assert.That(errors.GetProperty("host")[0].GetString(), Is.EqualTo("host debe ser un nombre de servidor o una dirección IP, sin protocolo ni espacios."));
            Assert.That(errors.GetProperty("port")[0].GetString(), Is.EqualTo("El campo port debe estar entre 1 y 65535."));
            Assert.That(errors.GetProperty("username")[0].GetString(), Is.EqualTo("El campo username es obligatorio."));
            Assert.That(errors.GetProperty("encryption")[0].GetString(), Is.EqualTo("encryption debe ser ssl, tls, starttls o none."));
            Assert.That(errors.GetProperty("from_address")[0].GetString(), Is.EqualTo("from address no es un correo válido"));
            Assert.That(json.RootElement.ToString(), Does.Not.Contain(Password));
            Assert.That(await RowAsync(nit), Is.Null, "A refused server is not stored.");
        });
    }

    [Test]
    public async Task Without_its_own_server_the_company_uses_the_platform_one_and_the_delete_goes_back_to_it()
    {
        var (company, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var owned = company;

        using var before = await company.GetAsync($"/api/v1/companies/{nit}/mail-settings");
        using var saved = await company.PutAsJsonAsync($"/api/v1/companies/{nit}/mail-settings", Body());
        using var deleted = await company.DeleteAsync($"/api/v1/companies/{nit}/mail-settings");
        using var again = await company.DeleteAsync($"/api/v1/companies/{nit}/mail-settings");
        using var after = await company.GetAsync($"/api/v1/companies/{nit}/mail-settings");

        using var beforeBody = await CompanyApiTests.Json(before);
        using var afterBody = await CompanyApiTests.Json(after);
        Assert.Multiple(async () =>
        {
            Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(beforeBody.RootElement.GetProperty("in_use").GetString(), Is.EqualTo("platform"));
            Assert.That(beforeBody.RootElement.GetProperty("host").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(beforeBody.RootElement.GetProperty("has_password").GetBoolean(), Is.False);
            Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(afterBody.RootElement.GetProperty("in_use").GetString(), Is.EqualTo("platform"));
            Assert.That(afterBody.RootElement.GetProperty("has_password").GetBoolean(), Is.False);
            Assert.That(await RowAsync(nit), Is.Null);
        });
    }

    [Test]
    public async Task Choosing_the_platform_default_keeps_the_own_server_saved_but_unused()
    {
        var (company, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var owned = company;

        using var saved = await company.PutAsJsonAsync($"/api/v1/companies/{nit}/mail-settings", new
        {
            host = "smtp.empresa.test", port = 465, username = "mailer", password = Password, encryption = "SSL", use_platform_default = true,
        });

        using var json = await CompanyApiTests.Json(saved);
        Assert.Multiple(() =>
        {
            Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK), json.RootElement.ToString());
            Assert.That(json.RootElement.GetProperty("in_use").GetString(), Is.EqualTo("platform"));
            Assert.That(json.RootElement.GetProperty("use_platform_default").GetBoolean(), Is.True);
            Assert.That(json.RootElement.GetProperty("encryption").GetString(), Is.EqualTo("ssl"), "The encryption is stored in lower case.");
            Assert.That(json.RootElement.GetProperty("from_address").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    public async Task Another_company_nit_answers_403_and_changes_nothing()
    {
        var (company, _) = await CompanyApiTests.CreateCompanyAsync();
        var (other, otherNit) = await CompanyApiTests.CreateCompanyAsync();
        using var owned = company;
        using var ownedOther = other;
        using var saved = await other.PutAsJsonAsync($"/api/v1/companies/{otherNit}/mail-settings", Body());

        using var get = await company.GetAsync($"/api/v1/companies/{otherNit}/mail-settings");
        using var put = await company.PutAsJsonAsync($"/api/v1/companies/{otherNit}/mail-settings", Body(host: "smtp.intruso.test"));
        using var delete = await company.DeleteAsync($"/api/v1/companies/{otherNit}/mail-settings");

        using var getBody = await CompanyApiTests.Json(get);
        var row = await RowAsync(otherNit);
        Assert.Multiple(() =>
        {
            Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(getBody.RootElement.GetProperty("code").GetString(), Is.EqualTo("company.forbidden"));
            Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(row!.Host, Is.EqualTo("smtp.empresa.test"));
        });
    }

    [Test]
    public async Task The_mail_settings_need_config_write()
    {
        var (company, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var owned = company;
        Guid companyId;
        await using (var scope = Api.OpenScope(null, null, ActorType.System))
        {
            companyId = await CompanyApiTests.CompanyIdAsync(scope.Db, nit);
        }

        using var reader = await Api.ClientWithNewKeyAsync(companyId, ApiScopes.DocumentsRead, ApiScopes.DocumentsWrite);
        using var get = await reader.GetAsync($"/api/v1/companies/{nit}/mail-settings");
        using var put = await reader.PutAsJsonAsync($"/api/v1/companies/{nit}/mail-settings", Body());
        using var delete = await reader.DeleteAsync($"/api/v1/companies/{nit}/mail-settings");

        Assert.Multiple(() =>
        {
            Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        });
    }

    private static async Task<CompanyMailSettings?> RowAsync(string nit)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var companyId = await CompanyApiTests.CompanyIdAsync(scope.Db, nit);
        return await scope.Db.Set<CompanyMailSettings>().IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(row => row.CompanyId == companyId);
    }
}
