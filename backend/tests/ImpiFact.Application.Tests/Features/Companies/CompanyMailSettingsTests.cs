using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Companies;
using ImpiFact.Application.Features.Companies.MailSettings;
using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Tests.Features.Companies;

public sealed class CompanyMailSettingsTests
{
    private const string Password = "Smtp-Secret-2026!";

    private readonly FakeSecretProtector _secrets = new();
    private FakeCompanyRepository _companies = null!;
    private Company _own = null!;

    [SetUp]
    public void SetUp()
    {
        _companies = new FakeCompanyRepository();
        _own = CompanySamples.Company();
        _companies.Stored.Add(_own);
    }

    private static SaveCompanyMailSettingsCommand Valid(string? password = Password) => new()
    {
        Host = "smtp.empresa.test",
        Port = 587,
        Username = "mailer",
        Password = password,
        Encryption = "tls",
        FromAddress = "facturacion@empresa.test",
        FromName = "Facturación",
    };

    [Test]
    public void A_complete_server_is_valid() =>
        Assert.That(new SaveCompanyMailSettingsValidator().Validate(Valid()).Errors, Is.Empty);

    [TestCase("", "El campo host es obligatorio.")]
    [TestCase("smtp://smtp.empresa.test", MailSettingsRules.InvalidHost)]
    [TestCase("smtp empresa", MailSettingsRules.InvalidHost)]
    [TestCase(" smtp.empresa.test", MailSettingsRules.InvalidHost)]
    public void The_host_is_a_bare_name_or_address(string host, string message) =>
        AssertSingleError(Valid() with { Host = host }, "host", message);

    [TestCase("127.0.0.1")]
    [TestCase("localhost")]
    [TestCase("::1")]
    public void An_ip_address_or_local_name_is_a_host(string host) =>
        Assert.That(new SaveCompanyMailSettingsValidator().Validate(Valid() with { Host = host }).Errors, Is.Empty);

    [TestCase(0)]
    [TestCase(65536)]
    [TestCase(-25)]
    public void The_port_is_between_1_and_65535(int port) =>
        AssertSingleError(Valid() with { Port = port }, "port", "El campo port debe estar entre 1 y 65535.");

    [Test]
    public void The_port_is_required() =>
        AssertSingleError(Valid() with { Port = null }, "port", "El campo port es obligatorio.");

    [TestCase("rot13")]
    [TestCase("")]
    public void The_encryption_is_ssl_tls_starttls_or_none(string encryption) =>
        AssertSingleError(Valid() with { Encryption = encryption }, "encryption",
            encryption.Length == 0 ? "El campo encryption es obligatorio." : "encryption debe ser ssl, tls, starttls o none.");

    [TestCase("SSL")]
    [TestCase("StartTLS")]
    [TestCase("none")]
    public void The_encryption_is_read_without_case(string encryption) =>
        Assert.That(new SaveCompanyMailSettingsValidator().Validate(Valid() with { Encryption = encryption }).Errors, Is.Empty);

    [Test]
    public void The_sender_address_is_an_email() =>
        AssertSingleError(Valid() with { FromAddress = "no-es-correo" }, "from_address", "from address no es un correo válido");

    [Test]
    public void The_username_is_required() =>
        AssertSingleError(Valid() with { Username = " " }, "username", "El campo username es obligatorio.");

    [Test]
    public void The_password_has_at_most_100_characters() =>
        AssertSingleError(Valid(new string('ñ', 101)), "password", "contraseña no debe ser mayor que 100 caracteres.");

    [Test]
    public void The_command_text_never_shows_the_password() =>
        Assert.That(Valid().ToString(), Does.Not.Contain(Password));

    [Test]
    public async Task The_password_is_encrypted_for_the_smtp_purpose_and_never_returned()
    {
        var result = await Save(Valid() with { Encryption = "STARTTLS" });

        var row = _companies.AddedOf<CompanyMailSettings>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(row.CompanyId, Is.EqualTo(_own.Id));
            Assert.That(row.PasswordCipher, Is.EqualTo(_secrets.Protect(SecretPurposes.SmtpPassword, Password)));
            Assert.That(row.Encryption, Is.EqualTo("starttls"));
            Assert.That(result.Value.HasPassword, Is.True);
            Assert.That(result.Value.InUse, Is.EqualTo(CompanyMailSettingsResponse.CompanyServer));
            Assert.That(typeof(CompanyMailSettingsResponse).GetProperties().Select(property => property.Name), Has.None.EqualTo("Password").And.None.Contains("Cipher"));
        });
    }

    [Test]
    public async Task The_first_save_needs_a_password_and_a_later_one_on_the_same_server_keeps_it()
    {
        var missing = await Save(Valid(password: null));
        _companies.Stored.Add(StoredServer());

        var kept = await Save(Valid(password: null) with { FromName = "Otra firma", UsePlatformDefault = true });

        var row = _companies.Stored.OfType<CompanyMailSettings>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(missing.Error.Fields.Single(), Is.EqualTo(new FieldError("password", MailSettingsRules.PasswordRequired)));
            Assert.That(_companies.Added, Is.Empty);
            Assert.That(kept.IsSuccess, Is.True);
            Assert.That((row.FromName, row.UsePlatformDefault), Is.EqualTo(("Otra firma", true)));
            Assert.That(row.PasswordCipher, Is.EqualTo("stored-cipher"));
        });
    }

    // End-of-7a verification, M2: the stored password only goes to the server it was given for.
    [TestCase("host")]
    [TestCase("port")]
    [TestCase("username")]
    [TestCase("encryption")]
    public async Task Changing_the_server_without_a_new_password_is_refused_and_changes_nothing(string field)
    {
        _companies.Stored.Add(StoredServer());
        var command = field switch
        {
            "host" => Valid(password: null) with { Host = "smtp.attacker.test" },
            "port" => Valid(password: null) with { Port = 2525 },
            "username" => Valid(password: null) with { Username = "otro" },
            _ => Valid(password: null) with { Encryption = "none" },
        };

        var refused = await Save(command);

        var row = _companies.Stored.OfType<CompanyMailSettings>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(refused.Error.Fields.Single(), Is.EqualTo(new FieldError("password", MailSettingsRules.PasswordRequiredForNewServer)));
            Assert.That((row.Host, row.Port, row.Username, row.Encryption, row.PasswordCipher),
                Is.EqualTo(("smtp.empresa.test", (int?)587, "mailer", "tls", "stored-cipher")));
        });
    }

    [Test]
    public async Task The_same_server_written_with_other_case_or_spaces_keeps_the_password()
    {
        _companies.Stored.Add(StoredServer());

        var kept = await Save(Valid(password: null) with { Host = "SMTP.Empresa.Test", Username = " mailer ", Encryption = "TLS" });

        Assert.That(kept.IsSuccess, Is.True);
        Assert.That(_companies.Stored.OfType<CompanyMailSettings>().Single().PasswordCipher, Is.EqualTo("stored-cipher"));
    }

    private CompanyMailSettings StoredServer() => new()
    {
        CompanyId = _own.Id, Host = "smtp.empresa.test", Port = 587, Username = "mailer", Encryption = "tls", PasswordCipher = "stored-cipher",
    };

    [Test]
    public async Task Another_company_nit_is_forbidden_without_touching_anything()
    {
        var result = await Save(Valid() with { Nit = "800111222" });

        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(CompanyErrors.ForeignNit));
            Assert.That(_companies.Added, Is.Empty);
        });
    }

    [Test]
    public async Task The_delete_removes_the_row_and_is_idempotent()
    {
        var tenant = CompanySamples.CompanyKey(_own.Id);
        var empty = await new DeleteCompanyMailSettingsHandler(_companies, tenant).HandleAsync(new DeleteCompanyMailSettingsCommand(_own.IdentificationNumber), CancellationToken.None);
        var row = new CompanyMailSettings { CompanyId = _own.Id, Host = "smtp.test" };
        _companies.Stored.Add(row);

        var deleted = await new DeleteCompanyMailSettingsHandler(_companies, tenant).HandleAsync(new DeleteCompanyMailSettingsCommand(_own.IdentificationNumber), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(empty.IsSuccess, Is.True);
            Assert.That(deleted.IsSuccess, Is.True);
            Assert.That(_companies.Removed, Is.EqualTo(new object[] { row }));
        });
    }

    [Test]
    public void A_server_chosen_over_by_the_platform_default_is_not_in_use() =>
        Assert.That(CompanyMailSettingsResponse.From(new CompanyMailSettings
        {
            CompanyId = Guid.NewGuid(), UsePlatformDefault = true, Host = "smtp.test", Port = 25, Username = "u", PasswordCipher = "c", Encryption = "none",
        }).InUse, Is.EqualTo(CompanyMailSettingsResponse.PlatformServer));

    private static void AssertSingleError(SaveCompanyMailSettingsCommand command, string field, string message)
    {
        var errors = new SaveCompanyMailSettingsValidator().Validate(command).Errors;
        Assert.That(errors.Select(error => (error.PropertyName, error.ErrorMessage)), Is.EqualTo(new[] { (field, message) }));
    }

    private Task<Result<CompanyMailSettingsResponse>> Save(SaveCompanyMailSettingsCommand command) =>
        new SaveCompanyMailSettingsHandler(_companies, CompanySamples.CompanyKey(_own.Id), _secrets)
            .HandleAsync(command with { Nit = command.Nit ?? _own.IdentificationNumber }, CancellationToken.None);
}
