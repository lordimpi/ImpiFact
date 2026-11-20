using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ImpiFact.Integration.Tests.Protection;

/// <summary>
/// Validación al arrancar del proveedor <c>KeyVault</c> (Azure Key Vault): sin la dirección del vault o sin la llave que
/// envuelve las llaves de Data Protection, la aplicación no arranca. No necesitan Docker: no hablan con el servicio.
/// </summary>
[TestFixture]
public sealed class KeyVaultSecretsOptionsTests
{
    [Test]
    public void Valid_key_vault_settings_pass_startup_validation_and_select_the_key_vault_store()
    {
        using var services = ProtectionServices.Build(ProtectionServices.ValidSettings(KeyVaultSettings()));

        Assert.DoesNotThrow(() => services.GetRequiredService<IStartupValidator>().Validate());
        Assert.That(services.GetRequiredService<ICertificateStore>().GetType().Name, Is.EqualTo("KeyVaultCertificateStore"));
    }

    [Test]
    public void Startup_fails_when_the_vault_uri_is_missing()
    {
        var settings = KeyVaultSettings();
        settings.Remove("Secrets:KeyVault:VaultUri");

        Assert.That(StartupFailure(settings), Does.Contain("Secrets:KeyVault:VaultUri is required when Secrets:Provider is KeyVault."));
    }

    [Test]
    public void Startup_fails_when_the_key_name_is_missing()
    {
        var settings = KeyVaultSettings();
        settings.Remove("Secrets:KeyVault:KeyName");

        Assert.That(StartupFailure(settings), Does.Contain("Secrets:KeyVault:KeyName is required when Secrets:Provider is KeyVault"));
    }

    [TestCase("impifact.vault.azure.net", TestName = "A_vault_uri_without_a_scheme_is_rejected")]
    [TestCase("http://impifact.vault.azure.net", TestName = "A_vault_uri_over_plain_http_is_rejected")]
    [TestCase("https://user:secret@impifact.vault.azure.net", TestName = "A_vault_uri_with_credentials_is_rejected")]
    [TestCase("https://impifact.vault.azure.net/?api-key=abc", TestName = "A_vault_uri_with_a_query_is_rejected")]
    public void Startup_fails_with_a_bad_vault_uri(string vaultUri)
    {
        var settings = KeyVaultSettings();
        settings["Secrets:KeyVault:VaultUri"] = vaultUri;

        var failure = StartupFailure(settings);

        Assert.Multiple(() =>
        {
            Assert.That(failure, Does.Contain("Secrets:KeyVault:VaultUri"));
            Assert.That(failure, Does.Not.Contain("secret@").And.Not.Contain("api-key=abc"), "The message never repeats a credential.");
        });
    }

    [Test]
    public void Startup_fails_with_an_invalid_key_name()
    {
        var settings = KeyVaultSettings();
        settings["Secrets:KeyVault:KeyName"] = "data_protection key";

        Assert.That(StartupFailure(settings), Does.Contain("is not a valid Key Vault key name"));
    }

    // Una dirección de Azure conserva la revisión del desafío de autenticación; la de un emulador no la necesita.
    [TestCase("https://impifact.vault.azure.net", true)]
    [TestCase("https://impifact.vault.usgovcloudapi.net", true)]
    [TestCase("https://localhost:8443", false)]
    [TestCase("https://lowkey-vault:8443", false)]
    [TestCase("https://vault.azure.net.example.com", false)]
    public void Only_azure_key_vault_hosts_count_as_azure(string vaultUri, bool azure)
    {
        Assert.That(ImpiFact.Infrastructure.Protection.KeyVaultClients.IsAzureKeyVault(new Uri(vaultUri)), Is.EqualTo(azure));
    }

    private static Dictionary<string, string?> KeyVaultSettings() => new()
    {
        ["Secrets:Provider"] = "KeyVault",
        ["Secrets:KeyVault:VaultUri"] = "https://impifact.vault.azure.net",
        ["Secrets:KeyVault:KeyName"] = "impifact-data-protection",
    };

    private static string StartupFailure(Dictionary<string, string?> secrets)
    {
        using var services = ProtectionServices.Build(ProtectionServices.ValidSettings(secrets));

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.That(exception!.OptionsType, Is.EqualTo(typeof(SecretsOptions)));
        return exception.Message;
    }
}
