using Azure.Security.KeyVault.Secrets;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Infrastructure.Protection;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Protection.KeyVault;

/// <summary>
/// Proveedor <c>KeyVault</c> contra lowkey-vault: las pruebas comunes de <see cref="CloudSecretsTests"/> y lo propio de
/// Key Vault (cada certificado es un secreto con la empresa en una etiqueta).
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class KeyVaultSecretsTests : CloudSecretsTests
{
    protected override string Scheme => "keyvault";

    protected override string Provider => "KeyVault";

    protected override ServiceProvider Build(string? connectionString = null, bool otherKey = false) =>
        LowkeyVault.Build(LowkeyVault.Settings(otherKey ? LowkeyVault.OtherKeyName : LowkeyVault.KeyName), connectionString);

    protected override ServiceProvider BuildWithMissingKey() => LowkeyVault.Build(LowkeyVault.Settings("missing-key"));

    protected override Task<string> CreateKeysDatabaseAsync() => LowkeyVault.CreateKeysDatabaseAsync();

    [Test]
    public async Task Each_certificate_is_a_vault_secret_tagged_with_its_company()
    {
        using var services = Build();
        var companyId = Guid.CreateVersion7();
        var secretRef = await services.GetRequiredService<ICertificateStore>().SaveAsync(companyId, TestPfx.Rsa(), TestPfx.Password, CancellationToken.None);
        var blobId = Guid.Parse(secretRef.Split('/')[1]);
        var client = new SecretClient(new Uri(LowkeyVault.VaultUri), LowkeyVault.TestClient.Credential!, LowkeyVault.Options(new SecretClientOptions()));

        var secret = (await client.GetSecretAsync($"impifact-cert-{companyId:N}-{blobId:N}")).Value;

        Assert.Multiple(() =>
        {
            Assert.That(secret.Properties.Tags["company"], Is.EqualTo(companyId.ToString("D")));
            Assert.That(secret.Properties.ContentType, Is.EqualTo("application/x-impifact-certificate;v=1"));
        });
    }

    // Sin credencial fija, el cliente entra con DefaultAzureCredential: aquí, con la identidad administrada que simula el
    // emulador (las mismas variables que pone App Service o Container Apps). Así se prueba la ruta de producción.
    [Test]
    [NonParallelizable]
    public async Task The_default_azure_credential_signs_in_with_the_managed_identity()
    {
        var endpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT");
        var header = Environment.GetEnvironmentVariable("IDENTITY_HEADER");
        Environment.SetEnvironmentVariable("IDENTITY_ENDPOINT", LowkeyVault.IdentityEndpoint);
        Environment.SetEnvironmentVariable("IDENTITY_HEADER", "lowkey-vault-test");
        try
        {
            using var services = ProtectionServices.Build(
                ProtectionServices.ValidSettings(LowkeyVault.Settings()),
                extra: collection => collection.AddSingleton(new KeyVaultTestClient(null, LowkeyVault.TestClient.Transport)));
            var store = services.GetRequiredService<ICertificateStore>();

            var secretRef = await store.SaveAsync(Guid.CreateVersion7(), TestPfx.Rsa(), TestPfx.Password, CancellationToken.None);

            Assert.That(secretRef, Does.StartWith("keyvault:"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("IDENTITY_ENDPOINT", endpoint);
            Environment.SetEnvironmentVariable("IDENTITY_HEADER", header);
        }
    }
}
