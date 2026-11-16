using ImpiFact.Application.Abstractions.Email;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Features.Documents.Email;

namespace ImpiFact.Application.Tests.Features.Documents.Email;

/// <summary>
/// Servidor de correo (SMTP, el protocolo de envío de correo) con que sale el correo de una empresa, como
/// <c>applyMailConfig</c> de ApiDian: el de la empresa si lo tiene completo (servidor, puerto, usuario, contraseña y
/// cifrado), si no el de la plataforma; el remitente de la empresa si lo tiene, si no el de la plataforma.
/// </summary>
public sealed class MailServerSelectorTests
{
    private static readonly Guid CompanyId = Guid.CreateVersion7();

    private static readonly MailServer Platform = new(
        "smtp.platform.test", 25, Username: null, Password: null, MailEncryption.None, "no-reply@platform.test", "ImpiFact");

    [Test]
    public async Task A_company_with_a_complete_server_sends_through_it_with_the_decrypted_password()
    {
        var server = await Select(Complete());

        Assert.Multiple(() =>
        {
            Assert.That(server.Host, Is.EqualTo("smtp.company.test"));
            Assert.That(server.Port, Is.EqualTo(587));
            Assert.That(server.Username, Is.EqualTo("mailer"));
            Assert.That(server.Password, Is.EqualTo("plain-password"));
            Assert.That(server.Encryption, Is.EqualTo(MailEncryption.StartTls));
            Assert.That(server.FromAddress, Is.EqualTo("billing@company.test"));
            Assert.That(server.FromName, Is.EqualTo("Company billing"));
        });
    }

    [TestCase("ssl", MailEncryption.SslOnConnect)]
    [TestCase("TLS", MailEncryption.StartTls)]
    [TestCase("starttls", MailEncryption.StartTls)]
    [TestCase("none", MailEncryption.None)]
    [TestCase("other", MailEncryption.Auto)]
    public async Task The_encryption_of_the_company_is_read_without_case(string encryption, MailEncryption expected) =>
        Assert.That((await Select(Complete() with { Encryption = encryption })).Encryption, Is.EqualTo(expected));

    [Test]
    public async Task A_company_without_its_own_sender_uses_the_platform_sender()
    {
        var server = await Select(Complete() with { FromAddress = " ", FromName = null });

        Assert.Multiple(() =>
        {
            Assert.That(server.Host, Is.EqualTo("smtp.company.test"));
            Assert.That(server.FromAddress, Is.EqualTo(Platform.FromAddress));
            Assert.That(server.FromName, Is.EqualTo(Platform.FromName));
        });
    }

    [Test]
    public async Task A_company_without_settings_uses_the_platform_server() =>
        Assert.That(await Select(null), Is.SameAs(Platform));

    [Test]
    public async Task A_company_that_chose_the_platform_default_uses_the_platform_server() =>
        Assert.That(await Select(Complete() with { UsePlatformDefault = true }), Is.SameAs(Platform));

    [Test]
    public async Task An_incomplete_company_server_is_ignored_like_validate_mail_server()
    {
        Assert.Multiple(async () =>
        {
            Assert.That(await Select(Complete() with { Host = "" }), Is.SameAs(Platform));
            Assert.That(await Select(Complete() with { Port = null }), Is.SameAs(Platform));
            Assert.That(await Select(Complete() with { Username = null }), Is.SameAs(Platform));
            Assert.That(await Select(Complete() with { PasswordCipher = null }), Is.SameAs(Platform));
            Assert.That(await Select(Complete() with { Encryption = "" }), Is.SameAs(Platform));
        });
    }

    [Test]
    public void The_password_never_appears_in_the_text_of_a_server() =>
        Assert.That((Platform with { Password = "plain-password" }).ToString(), Does.Not.Contain("plain-password").And.Contain(Platform.Host));

    private static CompanyMailServerSettings Complete() => new(
        UsePlatformDefault: false,
        Host: "smtp.company.test",
        Port: 587,
        Username: "mailer",
        PasswordCipher: "cipher:plain-password",
        Encryption: "tls",
        FromAddress: "billing@company.test",
        FromName: "Company billing");

    private static Task<MailServer> Select(CompanyMailServerSettings? settings) =>
        new MailServerSelector(new FakeMailSettings(settings), new FakePlatformMailServer(Platform), new PrefixProtector())
            .SelectAsync(CompanyId, CancellationToken.None);

    private sealed class FakeMailSettings(CompanyMailServerSettings? settings) : ICompanyMailSettingsReads
    {
        public Task<CompanyMailServerSettings?> GetAsync(Guid companyId, CancellationToken ct) =>
            Task.FromResult(companyId == CompanyId ? settings : null);
    }

    private sealed class FakePlatformMailServer(MailServer server) : IPlatformMailServer
    {
        public MailServer Default => server;
    }

    /// <summary>«Cifra» anteponiendo <c>cipher:</c>; solo descifra con el propósito de la contraseña SMTP.</summary>
    private sealed class PrefixProtector : ISecretProtector
    {
        public string Protect(string purpose, string plain) => "cipher:" + plain;

        public string Unprotect(string purpose, string cipher) =>
            purpose == SecretPurposes.SmtpPassword ? cipher["cipher:".Length..] : throw new InvalidOperationException("Wrong purpose.");
    }
}
