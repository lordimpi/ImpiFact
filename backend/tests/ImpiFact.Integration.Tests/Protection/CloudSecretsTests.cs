using System.Security.Cryptography;
using System.Xml.Linq;
using Dapper;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Infrastructure.Observability;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Protection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ImpiFact.Integration.Tests.Protection;

/// <summary>
/// Lo que cumple cada proveedor de secretos de la nube contra su emulador (Arquitectura detallada, secciones 5 y 19.2):
/// el almacén de certificados guarda y abre un PKCS#12 con su llave privada, ata cada referencia a su empresa, informa
/// una referencia que no existe y borra sin fallar dos veces; la llave del servicio envuelve las llaves de Data
/// Protection, que dos instancias comparten por la base sin dejarlas en claro, y que otra llave no abre; y la prueba de
/// salud distingue una llave que existe de una que falta. Cada proveedor la hereda en el espacio de nombres de su
/// emulador.
/// </summary>
public abstract class CloudSecretsTests
{
    private static readonly Guid CompanyA = Guid.Parse("0192f5a0-0000-7000-8000-0000000000aa");
    private static readonly Guid CompanyB = Guid.Parse("0192f5a0-0000-7000-8000-0000000000bb");

    /// <summary>El esquema de las referencias de este proveedor, por ejemplo <c>keyvault</c>.</summary>
    protected abstract string Scheme { get; }

    /// <summary>El valor de <c>Secrets:Provider</c>.</summary>
    protected abstract string Provider { get; }

    /// <summary>Servicios como los de la API contra el emulador; con <paramref name="otherKey"/>, configurados con otra llave que también existe.</summary>
    protected abstract ServiceProvider Build(string? connectionString = null, bool otherKey = false);

    /// <summary>Servicios configurados con una llave que no existe en el emulador.</summary>
    protected abstract ServiceProvider BuildWithMissingKey();

    protected abstract Task<string> CreateKeysDatabaseAsync();

    [Test]
    public async Task A_saved_certificate_loads_back_with_its_private_key()
    {
        using var services = Build();
        var store = services.GetRequiredService<ICertificateStore>();
        var pfx = TestPfx.Rsa();
        using var original = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(pfx, TestPfx.Password);

        var secretRef = await store.SaveAsync(CompanyA, pfx, TestPfx.Password, CancellationToken.None);
        using var loaded = await store.LoadAsync(CompanyA, secretRef, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(secretRef, Does.Match($"^{Scheme}:{CompanyA:D}/[0-9a-f-]{{36}}$"));
            Assert.That(secretRef, Does.Not.Contain(TestPfx.Password));
            Assert.That(loaded.Thumbprint, Is.EqualTo(original.Thumbprint));
            Assert.That(loaded.HasPrivateKey, Is.True);
        });
    }

    [Test]
    public async Task A_reference_is_rejected_for_another_company()
    {
        using var services = Build();
        var store = services.GetRequiredService<ICertificateStore>();
        var secretRef = await store.SaveAsync(CompanyA, TestPfx.Rsa(), TestPfx.Password, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync(CompanyB, secretRef, CancellationToken.None));
            Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(CompanyB, secretRef, CancellationToken.None));
            Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync(CompanyA, "local:" + secretRef.Split(':')[1], CancellationToken.None));
        });
    }

    [Test]
    public void A_reference_that_was_never_saved_is_reported_missing()
    {
        using var services = Build();
        var store = services.GetRequiredService<ICertificateStore>();

        Assert.ThrowsAsync<FileNotFoundException>(() => store.LoadAsync(CompanyA, $"{Scheme}:{CompanyA:D}/{Guid.NewGuid():D}", CancellationToken.None));
    }

    [Test]
    public async Task Deleting_removes_the_certificate_and_deleting_again_does_nothing()
    {
        using var services = Build();
        var store = services.GetRequiredService<ICertificateStore>();
        var secretRef = await store.SaveAsync(CompanyA, TestPfx.Rsa(), TestPfx.Password, CancellationToken.None);

        await store.DeleteAsync(CompanyA, secretRef, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<FileNotFoundException>(() => store.LoadAsync(CompanyA, secretRef, CancellationToken.None));
            Assert.DoesNotThrowAsync(() => store.DeleteAsync(CompanyA, secretRef, CancellationToken.None));
        });
    }

    // La llave de Data Protection se cifra con una llave nueva y esa llave la envuelve el servicio de la nube.
    [Test]
    public void A_data_protection_key_is_wrapped_by_the_cloud_key_and_unwrapped_back()
    {
        using var services = Build();
        var encryptor = new CloudKeyXmlEncryptor(services.GetRequiredService<IKeyEncryptionKey>());
        var key = new XElement("descriptor", new XElement("masterKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

        var encrypted = encryptor.Encrypt(key);
        var decrypted = new CloudKeyXmlDecryptor(services).Decrypt(encrypted.EncryptedElement);

        Assert.Multiple(() =>
        {
            Assert.That(XNode.DeepEquals(decrypted, key), Is.True);
            Assert.That(encrypted.DecryptorType, Is.EqualTo(typeof(CloudKeyXmlDecryptor)));
            Assert.That((string?)encrypted.EncryptedElement.Attribute("provider"), Is.EqualTo(Provider));
            Assert.That(encrypted.EncryptedElement.ToString(), Does.Not.Contain("masterKey"), "The key material must not be stored in clear.");
        });
    }

    [Test]
    public async Task A_secret_protected_by_one_instance_is_read_by_another_that_shares_the_database()
    {
        var connectionString = await CreateKeysDatabaseAsync();
        string cipher;
        using (var first = Build(connectionString))
        {
            cipher = first.GetRequiredService<ISecretProtector>().Protect(SecretPurposes.SoftwarePin, "12345");
        }

        using var second = Build(connectionString);
        var plain = second.GetRequiredService<ISecretProtector>().Unprotect(SecretPurposes.SoftwarePin, cipher);
        await using var connection = TestDatabase.Connect(connectionString);
        var rows = (await connection.QueryAsync<string>("select xml from data_protection_keys")).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo("12345"));
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0], Does.Contain(nameof(CloudKeyXmlDecryptor)).And.Contain($"provider=\"{Provider}\""));
            Assert.That(rows[0], Does.Not.Contain("<masterKey"), "The key material must not be stored in clear.");
        });
    }

    [Test]
    public async Task An_instance_configured_with_another_key_cannot_read_the_keys()
    {
        var connectionString = await CreateKeysDatabaseAsync();
        string cipher;
        using (var first = Build(connectionString))
        {
            cipher = first.GetRequiredService<ISecretProtector>().Protect(SecretPurposes.SoftwarePin, "12345");
        }

        using var intruder = Build(connectionString, otherKey: true);

        Assert.Throws(Is.InstanceOf<CryptographicException>(),
            () => intruder.GetRequiredService<ISecretProtector>().Unprotect(SecretPurposes.SoftwarePin, cipher));
    }

    [Test]
    public async Task The_readiness_check_is_healthy_with_the_key_and_unhealthy_without_it()
    {
        using var up = Build();
        using var down = BuildWithMissingKey();

        var healthy = await CheckAsync(up);
        var unhealthy = await CheckAsync(down);

        Assert.Multiple(() =>
        {
            Assert.That(healthy.Status, Is.EqualTo(HealthStatus.Healthy), healthy.Description);
            Assert.That(unhealthy.Status, Is.EqualTo(HealthStatus.Unhealthy));
            Assert.That(unhealthy.Exception, Is.Null, "The service answer never leaves the check.");
            Assert.That(unhealthy.Description, Is.EqualTo($"The {Provider} secrets provider cannot use its key."));
        });
    }

    private static Task<HealthCheckResult> CheckAsync(ServiceProvider services) =>
        new SecretsHealthCheck(services.GetRequiredService<IOptions<SecretsOptions>>(), services)
            .CheckHealthAsync(new HealthCheckContext());
}
