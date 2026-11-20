using Amazon.SecretsManager.Model;
using ImpiFact.Application.Abstractions.Protection;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Protection.Aws;

/// <summary>
/// Proveedor <c>AwsSecrets</c> contra Moto: las pruebas comunes de <see cref="CloudSecretsTests"/> y lo propio de Secrets
/// Manager (cada certificado es un secreto cifrado con la llave de KMS configurada, con la empresa en una etiqueta, y
/// borrar deja la ventana de recuperación).
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class AwsSecretsTests : CloudSecretsTests
{
    protected override string Scheme => "aws";

    protected override string Provider => "AwsSecrets";

    protected override ServiceProvider Build(string? connectionString = null, bool otherKey = false) =>
        MotoServer.Build(MotoServer.Settings(otherKey ? MotoServer.OtherKeyAlias : MotoServer.KeyAlias), connectionString);

    protected override ServiceProvider BuildWithMissingKey() => MotoServer.Build(MotoServer.Settings("alias/missing-key"));

    protected override Task<string> CreateKeysDatabaseAsync() => MotoServer.CreateKeysDatabaseAsync();

    [Test]
    public async Task Each_certificate_is_a_secret_encrypted_with_the_kms_key_and_tagged_with_its_company()
    {
        using var services = Build();
        var companyId = Guid.CreateVersion7();
        var secretRef = await services.GetRequiredService<ICertificateStore>().SaveAsync(companyId, TestPfx.Rsa(), TestPfx.Password, CancellationToken.None);
        var blobId = Guid.Parse(secretRef.Split('/')[1]);

        var secret = await MotoServer.SecretsClient.DescribeSecretAsync(new DescribeSecretRequest { SecretId = $"impifact/certificates/{companyId:D}/{blobId:D}" });

        Assert.Multiple(() =>
        {
            Assert.That(secret.KmsKeyId, Is.EqualTo(MotoServer.KeyAlias).Or.EqualTo(MotoServer.KeyArn));
            Assert.That(secret.Tags.Single(tag => tag.Key == "company").Value, Is.EqualTo(companyId.ToString("D")));
        });
    }

    [Test]
    public async Task Deleting_keeps_the_recovery_window()
    {
        using var services = Build();
        var store = services.GetRequiredService<ICertificateStore>();
        var companyId = Guid.CreateVersion7();
        var secretRef = await store.SaveAsync(companyId, TestPfx.Rsa(), TestPfx.Password, CancellationToken.None);
        var blobId = Guid.Parse(secretRef.Split('/')[1]);

        await store.DeleteAsync(companyId, secretRef, CancellationToken.None);
        var secret = await MotoServer.SecretsClient.DescribeSecretAsync(new DescribeSecretRequest { SecretId = $"impifact/certificates/{companyId:D}/{blobId:D}" });

        Assert.That(secret.DeletedDate, Is.Not.Null, "Scheduled for deletion, still recoverable.");
    }

    // Sin credenciales fijas, los clientes usan la cadena estándar de AWS: aquí, las variables de entorno con que se
    // corre la aplicación contra Moto (en la nube, el rol de la tarea). Así se prueba la ruta de producción.
    [Test]
    [NonParallelizable]
    public async Task The_default_credential_chain_signs_in_from_the_environment()
    {
        var accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
        var secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "moto-environment-access-key");
        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "moto-environment-secret-key");
        try
        {
            using var services = ProtectionServices.Build(ProtectionServices.ValidSettings(MotoServer.Settings()));
            var store = services.GetRequiredService<ICertificateStore>();

            var secretRef = await store.SaveAsync(Guid.CreateVersion7(), TestPfx.Rsa(), TestPfx.Password, CancellationToken.None);

            Assert.That(secretRef, Does.StartWith("aws:"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", accessKey);
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", secretKey);
        }
    }
}
