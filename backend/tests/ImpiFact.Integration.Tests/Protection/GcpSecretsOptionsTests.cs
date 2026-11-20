using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ImpiFact.Integration.Tests.Protection;

/// <summary>
/// Validación al arrancar del proveedor <c>GcpSecrets</c> (Google Secret Manager con una llave de Cloud KMS): sin el
/// proyecto o sin la llave de Cloud KMS, la aplicación no arranca. No necesitan Docker: no hablan con el servicio.
/// </summary>
[TestFixture]
public sealed class GcpSecretsOptionsTests
{
    [Test]
    public void Valid_gcp_settings_pass_startup_validation_and_select_the_secret_manager_store()
    {
        using var services = ProtectionServices.Build(ProtectionServices.ValidSettings(GcpSettings()));

        Assert.DoesNotThrow(() => services.GetRequiredService<IStartupValidator>().Validate());
        Assert.That(services.GetRequiredService<ICertificateStore>().GetType().Name, Is.EqualTo("GcpSecretManagerCertificateStore"));
    }

    [Test]
    public void An_emulator_endpoint_passes_startup_validation()
    {
        var settings = GcpSettings();
        settings["Secrets:GcpSecrets:Endpoint"] = "http://127.0.0.1:4588";
        using var services = ProtectionServices.Build(ProtectionServices.ValidSettings(settings));

        Assert.DoesNotThrow(() => services.GetRequiredService<IStartupValidator>().Validate());
    }

    [Test]
    public void Startup_fails_when_the_project_is_missing()
    {
        var settings = GcpSettings();
        settings.Remove("Secrets:GcpSecrets:ProjectId");

        Assert.That(StartupFailure(settings), Does.Contain("Secrets:GcpSecrets:ProjectId is required when Secrets:Provider is GcpSecrets."));
    }

    [Test]
    public void Startup_fails_when_the_kms_key_is_missing()
    {
        var settings = GcpSettings();
        settings.Remove("Secrets:GcpSecrets:KmsKeyName");

        Assert.That(StartupFailure(settings), Does.Contain("Secrets:GcpSecrets:KmsKeyName is required when Secrets:Provider is GcpSecrets"));
    }

    [TestCase("Impifact", TestName = "A_gcp_project_with_upper_case_is_rejected")]
    [TestCase("ab1", TestName = "A_gcp_project_that_is_too_short_is_rejected")]
    [TestCase("impifact_prod", TestName = "A_gcp_project_with_an_underscore_is_rejected")]
    public void Startup_fails_with_an_invalid_project(string project)
    {
        var settings = GcpSettings();
        settings["Secrets:GcpSecrets:ProjectId"] = project;

        Assert.That(StartupFailure(settings), Does.Contain("is not a valid Google Cloud project id"));
    }

    [TestCase("impifact-data-protection", TestName = "A_gcp_kms_key_that_is_only_a_name_is_rejected")]
    [TestCase("projects/impifact-prod/locations/us/keyRings/impifact", TestName = "A_gcp_kms_key_ring_without_a_key_is_rejected")]
    [TestCase("projects/impifact-prod/locations/us/keyRings/impifact/cryptoKeys/dp/cryptoKeyVersions/1", TestName = "A_gcp_kms_key_version_is_rejected")]
    public void Startup_fails_with_an_invalid_kms_key_name(string keyName)
    {
        var settings = GcpSettings();
        settings["Secrets:GcpSecrets:KmsKeyName"] = keyName;

        Assert.That(StartupFailure(settings), Does.Contain("is not a Cloud KMS key name"));
    }

    [TestCase("127.0.0.1:4588", TestName = "A_gcp_secrets_endpoint_without_a_scheme_is_rejected")]
    [TestCase("http://user:secret@127.0.0.1:4588", TestName = "A_gcp_secrets_endpoint_with_credentials_is_rejected")]
    [TestCase("http://127.0.0.1:4588/?key=abc", TestName = "A_gcp_secrets_endpoint_with_a_query_is_rejected")]
    public void Startup_fails_with_a_bad_endpoint(string endpoint)
    {
        var settings = GcpSettings();
        settings["Secrets:GcpSecrets:Endpoint"] = endpoint;

        var failure = StartupFailure(settings);

        Assert.Multiple(() =>
        {
            Assert.That(failure, Does.Contain("Secrets:GcpSecrets:Endpoint"));
            Assert.That(failure, Does.Not.Contain("secret@").And.Not.Contain("key=abc"), "The message never repeats a credential.");
        });
    }

    private static Dictionary<string, string?> GcpSettings() => new()
    {
        ["Secrets:Provider"] = "GcpSecrets",
        ["Secrets:GcpSecrets:ProjectId"] = "impifact-prod",
        ["Secrets:GcpSecrets:KmsKeyName"] = "projects/impifact-prod/locations/us-central1/keyRings/impifact/cryptoKeys/data-protection",
    };

    private static string StartupFailure(Dictionary<string, string?> secrets)
    {
        using var services = ProtectionServices.Build(ProtectionServices.ValidSettings(secrets));

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.That(exception!.OptionsType, Is.EqualTo(typeof(SecretsOptions)));
        return exception.Message;
    }
}
