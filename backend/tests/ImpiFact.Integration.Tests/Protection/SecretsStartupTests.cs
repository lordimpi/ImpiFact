using System.Security.Cryptography;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ImpiFact.Integration.Tests.Protection;

/// <summary>
/// Con el proveedor <c>LocalAesGcm</c>, la aplicación no arranca sin una llave maestra válida (Arquitectura
/// detallada, sección 11: el arranque falla si falta un secreto). No necesitan Docker.
/// </summary>
[TestFixture]
public sealed class SecretsStartupTests
{
    private ProtectionScratch scratch = null!;

    [SetUp]
    public void CreateScratch() => scratch = new ProtectionScratch();

    [TearDown]
    public void DeleteScratch() => scratch.Dispose();

    [Test]
    public void A_valid_master_key_file_passes_startup_validation()
    {
        using var services = ProtectionServices.Build(ProtectionServices.ValidSettings(scratch.LocalAesGcmSettings()));

        Assert.DoesNotThrow(() => services.GetRequiredService<IStartupValidator>().Validate());
    }

    [Test]
    public void Startup_fails_when_the_master_key_file_is_not_configured()
    {
        var secrets = scratch.LocalAesGcmSettings();
        secrets.Remove("Secrets:LocalAesGcm:MasterKeyFile");

        var failure = StartupFailure(secrets);

        Assert.That(failure, Does.Contain("MasterKeyFile"));
    }

    // Una carpeta que no se puede crear (aquí, porque ya hay un archivo con ese nombre) debe fallar al arrancar, no en
    // la primera carga de un certificado.
    [Test]
    public void Startup_fails_when_the_certificate_directory_cannot_be_created()
    {
        var blocked = Path.Combine(scratch.Root, "not-a-directory");
        File.WriteAllText(blocked, "a file, not a folder");
        var secrets = scratch.LocalAesGcmSettings();
        secrets["Secrets:LocalAesGcm:CertificateDirectory"] = Path.Combine(blocked, "certificates");

        var failure = StartupFailure(secrets);

        Assert.That(failure, Does.Contain("CertificateDirectory"));
    }

    [Test]
    public void Startup_fails_when_the_master_key_file_does_not_exist()
    {
        var failure = StartupFailure(scratch.LocalAesGcmSettings(Path.Combine(scratch.Root, "missing.key")));

        Assert.That(failure, Does.Contain("does not exist"));
    }

    [TestCase(16)]
    [TestCase(31)]
    [TestCase(33)]
    public void Startup_fails_when_the_master_key_is_not_32_bytes(int length)
    {
        var key = RandomNumberGenerator.GetBytes(length);
        var path = scratch.WriteMasterKey("short.key", key);

        var failure = StartupFailure(scratch.LocalAesGcmSettings(path));

        Assert.Multiple(() =>
        {
            Assert.That(failure, Does.Contain("32 bytes"));
            Assert.That(failure, Does.Not.Contain(Convert.ToBase64String(key)), "The key must never appear in messages.");
        });
    }

    [Test]
    public void Startup_fails_when_the_master_key_is_not_base64()
    {
        var path = Path.Combine(scratch.Root, "text.key");
        File.WriteAllText(path, "this is not base64 !!");

        var failure = StartupFailure(scratch.LocalAesGcmSettings(path));

        Assert.Multiple(() =>
        {
            Assert.That(failure, Does.Contain("32 bytes"));
            Assert.That(failure, Does.Not.Contain("this is not base64"));
        });
    }

    [Test]
    public void Startup_fails_when_the_certificate_directory_is_not_configured()
    {
        var secrets = scratch.LocalAesGcmSettings();
        secrets.Remove("Secrets:LocalAesGcm:CertificateDirectory");

        var failure = StartupFailure(secrets);

        Assert.That(failure, Does.Contain("CertificateDirectory"));
    }

    [TestCase("KeyVault")]
    [TestCase("AwsSecrets")]
    [TestCase("GcpSecrets")]
    public void Startup_fails_for_cloud_providers_that_are_not_available_yet(string provider)
    {
        var failure = StartupFailure(new() { ["Secrets:Provider"] = provider });

        Assert.That(failure, Does.Contain(provider).And.Contain("not available"));
    }

    /// <summary>Corre la validación del arranque y devuelve el mensaje de la falla de <see cref="SecretsOptions"/>.</summary>
    private static string StartupFailure(Dictionary<string, string?> secrets)
    {
        using var services = ProtectionServices.Build(ProtectionServices.ValidSettings(secrets));

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.That(exception!.OptionsType, Is.EqualTo(typeof(SecretsOptions)));
        return exception.Message;
    }
}
