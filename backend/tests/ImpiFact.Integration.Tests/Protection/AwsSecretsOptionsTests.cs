using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ImpiFact.Integration.Tests.Protection;

/// <summary>
/// Validación al arrancar del proveedor <c>AwsSecrets</c> (AWS Secrets Manager con una llave de KMS): sin región (o URL de
/// un emulador) o sin la llave de KMS, la aplicación no arranca. No necesitan Docker: no hablan con el servicio.
/// </summary>
[TestFixture]
public sealed class AwsSecretsOptionsTests
{
    [Test]
    public void Valid_aws_settings_pass_startup_validation_and_select_the_secrets_manager_store()
    {
        using var services = ProtectionServices.Build(ProtectionServices.ValidSettings(AwsSettings()));

        Assert.DoesNotThrow(() => services.GetRequiredService<IStartupValidator>().Validate());
        Assert.That(services.GetRequiredService<ICertificateStore>().GetType().Name, Is.EqualTo("AwsSecretsManagerCertificateStore"));
    }

    [Test]
    public void An_emulator_url_passes_startup_validation_without_a_region()
    {
        var settings = AwsSettings();
        settings.Remove("Secrets:AwsSecrets:Region");
        settings["Secrets:AwsSecrets:ServiceUrl"] = "http://127.0.0.1:5000";
        using var services = ProtectionServices.Build(ProtectionServices.ValidSettings(settings));

        Assert.DoesNotThrow(() => services.GetRequiredService<IStartupValidator>().Validate());
    }

    [Test]
    public void Startup_fails_when_the_kms_key_is_missing()
    {
        var settings = AwsSettings();
        settings.Remove("Secrets:AwsSecrets:KmsKeyId");

        Assert.That(StartupFailure(settings), Does.Contain("Secrets:AwsSecrets:KmsKeyId is required when Secrets:Provider is AwsSecrets"));
    }

    [Test]
    public void Startup_fails_without_a_region_when_no_service_url_is_set()
    {
        var settings = AwsSettings();
        settings.Remove("Secrets:AwsSecrets:Region");

        Assert.That(StartupFailure(settings), Does.Contain("Secrets:AwsSecrets:Region is required when Secrets:AwsSecrets:ServiceUrl is not set."));
    }

    [Test]
    public void Startup_fails_with_a_malformed_region()
    {
        var settings = AwsSettings();
        settings["Secrets:AwsSecrets:Region"] = "Virginia";

        Assert.That(StartupFailure(settings), Does.Contain("is not a valid AWS region"));
    }

    [TestCase("127.0.0.1:5000", TestName = "An_aws_secrets_service_url_without_a_scheme_is_rejected")]
    [TestCase("ftp://127.0.0.1:5000", TestName = "An_aws_secrets_service_url_that_is_not_http_is_rejected")]
    [TestCase("http://AKIA:secret@127.0.0.1:5000", TestName = "An_aws_secrets_service_url_with_credentials_is_rejected")]
    [TestCase("http://127.0.0.1:5000/?X-Amz-Signature=abc", TestName = "An_aws_secrets_service_url_with_a_query_is_rejected")]
    public void Startup_fails_with_a_bad_service_url(string serviceUrl)
    {
        var settings = AwsSettings();
        settings["Secrets:AwsSecrets:ServiceUrl"] = serviceUrl;

        var failure = StartupFailure(settings);

        Assert.Multiple(() =>
        {
            Assert.That(failure, Does.Contain("Secrets:AwsSecrets:ServiceUrl"));
            Assert.That(failure, Does.Not.Contain("secret@").And.Not.Contain("Signature=abc"), "The message never repeats a credential.");
        });
    }

    private static Dictionary<string, string?> AwsSettings() => new()
    {
        ["Secrets:Provider"] = "AwsSecrets",
        ["Secrets:AwsSecrets:Region"] = "us-east-1",
        ["Secrets:AwsSecrets:KmsKeyId"] = "alias/impifact-data-protection",
    };

    private static string StartupFailure(Dictionary<string, string?> secrets)
    {
        using var services = ProtectionServices.Build(ProtectionServices.ValidSettings(secrets));

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.That(exception!.OptionsType, Is.EqualTo(typeof(SecretsOptions)));
        return exception.Message;
    }
}
