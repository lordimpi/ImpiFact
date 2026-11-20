using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ImpiFact.Integration.Tests.Storage;

/// <summary>
/// Validación al arrancar del proveedor <c>Gcs</c> (Google Cloud Storage): sin un bucket con nombre válido, con una
/// dirección de emulador inválida o con una vigencia de URL firmada fuera de rango, la aplicación no arranca. No
/// necesitan Docker: no hablan con el servicio.
/// </summary>
[TestFixture]
public sealed class GcsStorageOptionsTests
{
    [Test]
    public void Valid_gcs_settings_pass_startup_validation_and_select_the_gcs_storage()
    {
        using var services = StorageScratch.Build(GcsSettings());

        Assert.DoesNotThrow(() => services.GetRequiredService<IStartupValidator>().Validate());
        Assert.That(services.GetRequiredService<IBlobStorage>().Provider, Is.EqualTo(StorageProvider.Gcs));
    }

    [Test]
    public void An_emulator_endpoint_passes_startup_validation()
    {
        var settings = GcsSettings();
        settings["Storage:Gcs:Endpoint"] = "http://127.0.0.1:4443";
        using var services = StorageScratch.Build(settings);

        Assert.DoesNotThrow(() => services.GetRequiredService<IStartupValidator>().Validate());
    }

    [Test]
    public void Startup_fails_when_the_bucket_is_missing()
    {
        var settings = GcsSettings();
        settings.Remove("Storage:Gcs:Bucket");

        Assert.That(StartupFailure(settings), Does.Contain("Storage:Gcs:Bucket is required when Storage:Provider is Gcs."));
    }

    [TestCase("ab", TestName = "A_gcs_bucket_name_that_is_too_short_is_rejected")]
    [TestCase("Documents", TestName = "A_gcs_bucket_name_with_upper_case_is_rejected")]
    [TestCase("-documents", TestName = "A_gcs_bucket_name_starting_with_a_hyphen_is_rejected")]
    [TestCase("documents/2026", TestName = "A_gcs_bucket_name_with_a_slash_is_rejected")]
    [TestCase("goog-documents", TestName = "A_gcs_bucket_name_starting_with_goog_is_rejected")]
    public void Startup_fails_with_an_invalid_bucket_name(string bucket)
    {
        var settings = GcsSettings();
        settings["Storage:Gcs:Bucket"] = bucket;

        Assert.That(StartupFailure(settings), Does.Contain("is not a valid Cloud Storage bucket name"));
    }

    [TestCase("127.0.0.1:4443", TestName = "A_gcs_endpoint_without_a_scheme_is_rejected")]
    [TestCase("ftp://127.0.0.1:4443", TestName = "A_gcs_endpoint_that_is_not_http_is_rejected")]
    [TestCase("http://user:secret@127.0.0.1:4443", TestName = "A_gcs_endpoint_with_credentials_is_rejected")]
    [TestCase("http://127.0.0.1:4443/?key=abc", TestName = "A_gcs_endpoint_with_a_query_is_rejected")]
    public void Startup_fails_with_a_bad_endpoint(string endpoint)
    {
        var settings = GcsSettings();
        settings["Storage:Gcs:Endpoint"] = endpoint;

        var failure = StartupFailure(settings);

        Assert.Multiple(() =>
        {
            Assert.That(failure, Does.Contain("Storage:Gcs:Endpoint"));
            Assert.That(failure, Does.Not.Contain("secret").And.Not.Contain("key=abc"), "The message never repeats a credential.");
        });
    }

    [TestCase("0")]
    [TestCase("10081")]
    public void Startup_fails_when_the_signed_url_limit_is_out_of_range(string minutes)
    {
        var settings = GcsSettings();
        settings["Storage:Gcs:SignedUrlMaxMinutes"] = minutes;

        Assert.That(StartupFailure(settings), Does.Contain("Storage:Gcs:SignedUrlMaxMinutes"));
    }

    private static Dictionary<string, string?> GcsSettings() => new()
    {
        ["Storage:Provider"] = "Gcs",
        ["Storage:Gcs:Bucket"] = "impifact-documents",
    };

    private static string StartupFailure(Dictionary<string, string?> settings)
    {
        using var services = StorageScratch.Build(settings);
        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());
        return exception!.Message;
    }
}
