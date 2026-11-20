using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ImpiFact.Integration.Tests.Storage;

/// <summary>
/// Validación al arrancar del proveedor <c>AzureBlob</c> (Azure Blob Storage, el almacenamiento de objetos de Azure):
/// sin la dirección de la cuenta o sin un contenedor (el equivalente del bucket) válido, la aplicación no arranca. No
/// necesitan Docker: no hablan con el servicio.
/// </summary>
[TestFixture]
public sealed class AzureBlobStorageOptionsTests
{
    [Test]
    public void Valid_azure_settings_pass_startup_validation_and_select_the_azure_blob_storage()
    {
        using var services = StorageScratch.Build(AzureSettings());

        Assert.DoesNotThrow(() => services.GetRequiredService<IStartupValidator>().Validate());
        Assert.That(services.GetRequiredService<IBlobStorage>().Provider, Is.EqualTo(StorageProvider.AzureBlob));
    }

    [Test]
    public void An_emulator_endpoint_over_http_passes_startup_validation()
    {
        var settings = AzureSettings();
        settings["Storage:AzureBlob:ServiceUri"] = "http://127.0.0.1:10000/devstoreaccount1";
        using var services = StorageScratch.Build(settings);

        Assert.DoesNotThrow(() => services.GetRequiredService<IStartupValidator>().Validate());
    }

    // Con la cuenta de desarrollo de Azurite el SDK firma en este proceso, sin llamar al servicio.
    [Test]
    public void The_emulator_account_signs_links_for_the_azurite_host()
    {
        var settings = AzureSettings();
        settings["Storage:AzureBlob:ServiceUri"] = "http://azurite:10000/devstoreaccount1";
        settings["Storage:AzureBlob:UseEmulatorAccount"] = "true";
        using var services = StorageScratch.Build(settings);

        services.GetRequiredService<IStartupValidator>().Validate();
        var url = services.GetRequiredService<IBlobStorage>().CreateSignedUrl("company/invoice.pdf", TimeSpan.FromMinutes(5));

        Assert.Multiple(() =>
        {
            Assert.That(url.GetLeftPart(UriPartial.Path), Is.EqualTo("http://azurite:10000/devstoreaccount1/impifact-documents/company/invoice.pdf"));
            Assert.That(url.Query, Does.Contain("sig="));
        });
    }

    [TestCase("https://impifactdocs.blob.core.windows.net", TestName = "The_emulator_account_is_rejected_for_an_azure_account")]
    [TestCase("http://127.0.0.1:10010/devstoreaccount1", TestName = "The_emulator_account_is_rejected_on_another_port")]
    [TestCase("http://127.0.0.1:10000/impifactdocs", TestName = "The_emulator_account_is_rejected_for_another_account")]
    public void The_emulator_account_only_works_with_azurite(string serviceUri)
    {
        var settings = AzureSettings();
        settings["Storage:AzureBlob:ServiceUri"] = serviceUri;
        settings["Storage:AzureBlob:UseEmulatorAccount"] = "true";

        Assert.That(StartupFailure(settings), Does.Contain("Storage:AzureBlob:UseEmulatorAccount only works with Azurite"));
    }

    [Test]
    public void Startup_fails_when_the_container_is_missing()
    {
        var settings = AzureSettings();
        settings.Remove("Storage:AzureBlob:Container");

        Assert.That(StartupFailure(settings), Does.Contain("Storage:AzureBlob:Container is required when Storage:Provider is AzureBlob."));
    }

    [Test]
    public void Startup_fails_when_the_service_uri_is_missing()
    {
        var settings = AzureSettings();
        settings.Remove("Storage:AzureBlob:ServiceUri");

        Assert.That(StartupFailure(settings), Does.Contain("Storage:AzureBlob:ServiceUri is required when Storage:Provider is AzureBlob."));
    }

    [TestCase("ab", TestName = "A_container_name_that_is_too_short_is_rejected")]
    [TestCase("Documents", TestName = "A_container_name_with_upper_case_is_rejected")]
    [TestCase("documents_2026", TestName = "A_container_name_with_an_underscore_is_rejected")]
    [TestCase("-documents", TestName = "A_container_name_starting_with_a_hyphen_is_rejected")]
    [TestCase("impifact--documents", TestName = "A_container_name_with_two_hyphens_in_a_row_is_rejected")]
    [TestCase("impifact.documents", TestName = "A_container_name_with_a_dot_is_rejected")]
    public void Startup_fails_with_an_invalid_container_name(string container)
    {
        var settings = AzureSettings();
        settings["Storage:AzureBlob:Container"] = container;

        Assert.That(StartupFailure(settings), Does.Contain("is not a valid Azure Blob container name"));
    }

    // Una SAS (firma de acceso compartido) o una llave en la URL serían una credencial fija en la configuración.
    [TestCase("impifact.blob.core.windows.net", TestName = "A_service_uri_without_a_scheme_is_rejected")]
    [TestCase("ftp://impifact.blob.core.windows.net", TestName = "A_service_uri_that_is_not_http_is_rejected")]
    [TestCase("https://user:secret@impifact.blob.core.windows.net", TestName = "A_service_uri_with_credentials_is_rejected")]
    [TestCase("https://impifact.blob.core.windows.net/?sv=2025-01-05&sig=abc", TestName = "A_service_uri_with_a_sas_token_is_rejected")]
    public void Startup_fails_with_a_bad_service_uri(string serviceUri)
    {
        var settings = AzureSettings();
        settings["Storage:AzureBlob:ServiceUri"] = serviceUri;

        var failure = StartupFailure(settings);

        Assert.Multiple(() =>
        {
            Assert.That(failure, Does.Contain("Storage:AzureBlob:ServiceUri"));
            Assert.That(failure, Does.Not.Contain("secret").And.Not.Contain("sig=abc"), "The message never repeats a credential.");
        });
    }

    [TestCase("0")]
    [TestCase("10081")]
    public void Startup_fails_when_the_signed_url_limit_is_out_of_range(string minutes)
    {
        var settings = AzureSettings();
        settings["Storage:AzureBlob:SignedUrlMaxMinutes"] = minutes;

        Assert.That(StartupFailure(settings), Does.Contain("Storage:AzureBlob:SignedUrlMaxMinutes"));
    }

    private static Dictionary<string, string?> AzureSettings() => new()
    {
        ["Storage:Provider"] = "AzureBlob",
        ["Storage:AzureBlob:ServiceUri"] = "https://impifactdocs.blob.core.windows.net",
        ["Storage:AzureBlob:Container"] = "impifact-documents",
    };

    private static string StartupFailure(Dictionary<string, string?> settings)
    {
        using var services = StorageScratch.Build(settings);
        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());
        return exception!.Message;
    }
}
