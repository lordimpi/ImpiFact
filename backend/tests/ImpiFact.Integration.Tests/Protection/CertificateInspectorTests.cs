using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ImpiFact.Application.Abstractions.Protection;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Protection;

/// <summary>
/// Revisión de un PKCS#12 antes de guardarlo: contraseña, llave privada RSA, vencimiento y los días restantes
/// con la regla del PHP (<c>certificate_days_left</c>, Reglas del PHP sección 5.3). No necesitan Docker.
/// </summary>
[TestFixture]
public sealed class CertificateInspectorTests
{
    /// <summary>15 de enero de 2026 a las 05:00 en Colombia (UTC-5).</summary>
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset LongAgo = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private ProtectionScratch scratch = null!;
    private ServiceProvider services = null!;
    private ICertificateInspector inspector = null!;

    [SetUp]
    public void BuildInspector()
    {
        scratch = new ProtectionScratch();
        services = ProtectionServices.Build(ProtectionServices.ValidSettings(scratch.LocalAesGcmSettings()), new FixedProtectionClock(Now));
        inspector = services.GetRequiredService<ICertificateInspector>();
    }

    [TearDown]
    public void Cleanup()
    {
        services.Dispose();
        scratch.Dispose();
    }

    [Test]
    public void A_valid_certificate_returns_its_public_details()
    {
        var notAfter = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var pfx = TestPfx.Rsa(LongAgo, notAfter);
        using var expected = X509CertificateLoader.LoadPkcs12(pfx, TestPfx.Password);

        var result = inspector.Inspect(pfx, TestPfx.Password);

        Assert.That(result.IsSuccess, Is.True);
        var details = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(details.Thumbprint, Is.EqualTo(expected.GetCertHashString(HashAlgorithmName.SHA256)));
            Assert.That(details.Thumbprint, Has.Length.EqualTo(64));
            Assert.That(details.Subject, Is.EqualTo("CN=ImpiFact Test Company, O=ImpiFact Pruebas, C=CO"));
            Assert.That(details.Issuer, Is.EqualTo(details.Subject), "Self-signed: the issuer is the subject.");
            Assert.That(details.SerialNumber, Is.EqualTo(expected.SerialNumber));
            Assert.That(details.NotBefore, Is.EqualTo(LongAgo));
            Assert.That(details.NotAfter, Is.EqualTo(notAfter));
            Assert.That(details.DaysLeft, Is.EqualTo(45), "From 2026-01-15 to 2026-03-01.");
        });
    }

    [Test]
    public void Days_left_counts_calendar_days_in_colombia_time()
    {
        // 2026-01-16 01:00 en Colombia: falta un día aunque solo falten 20 horas.
        var result = inspector.Inspect(TestPfx.Rsa(LongAgo, new DateTimeOffset(2026, 1, 16, 6, 0, 0, TimeSpan.Zero)), TestPfx.Password);

        Assert.That(result.Value.DaysLeft, Is.EqualTo(1));
    }

    [Test]
    public void A_certificate_that_ends_today_in_colombia_is_expired()
    {
        // 2026-01-16 03:00 UTC ya es el 16 en UTC, pero en Colombia sigue siendo el 15 (22:00): cero días, vencido.
        var result = inspector.Inspect(TestPfx.Rsa(LongAgo, new DateTimeOffset(2026, 1, 16, 3, 0, 0, TimeSpan.Zero)), TestPfx.Password);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error, Is.EqualTo(CertificateErrors.Expired));
    }

    [Test]
    public void An_expired_certificate_is_rejected()
    {
        var result = inspector.Inspect(TestPfx.Rsa(LongAgo, new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero)), TestPfx.Password);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error, Is.EqualTo(CertificateErrors.Expired));
    }

    [Test]
    public void A_wrong_password_is_rejected()
    {
        var result = inspector.Inspect(TestPfx.Rsa(), "wrong-password");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error, Is.EqualTo(CertificateErrors.InvalidPasswordOrFormat));
    }

    [Test]
    public void Bytes_that_are_not_pkcs12_are_rejected()
    {
        var result = inspector.Inspect(RandomNumberGenerator.GetBytes(256), TestPfx.Password);

        Assert.That(result.Error, Is.EqualTo(CertificateErrors.InvalidPasswordOrFormat));
    }

    [Test]
    public void A_certificate_without_private_key_is_rejected()
    {
        var result = inspector.Inspect(TestPfx.WithoutPrivateKey(), TestPfx.Password);

        Assert.That(result.Error, Is.EqualTo(CertificateErrors.MissingRsaPrivateKey));
    }

    [Test]
    public void A_certificate_with_a_non_rsa_private_key_is_rejected()
    {
        var result = inspector.Inspect(TestPfx.Ecdsa(), TestPfx.Password);

        Assert.That(result.Error, Is.EqualTo(CertificateErrors.MissingRsaPrivateKey));
    }
}
