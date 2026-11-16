using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ImpiFact.Integration.Tests.Storage;

/// <summary>
/// Validación al arrancar del proveedor S3 (Amazon Simple Storage Service, el almacenamiento de objetos de AWS, o
/// un servicio compatible como MinIO): sin un bucket (el contenedor de objetos) válido, con una región mal escrita o
/// con una URL de servicio inválida, la aplicación no arranca. No necesitan Docker: no hablan con el servicio.
/// </summary>
[TestFixture]
public sealed class S3StorageOptionsTests
{
    [Test]
    public void Valid_aws_settings_pass_startup_validation_and_select_the_s3_storage()
    {
        using var services = StorageScratch.Build(AwsSettings());

        Assert.DoesNotThrow(() => services.GetRequiredService<IStartupValidator>().Validate());
        Assert.That(services.GetRequiredService<IBlobStorage>().Provider, Is.EqualTo(StorageProvider.S3));
    }

    [Test]
    public void Valid_s3_compatible_settings_pass_startup_validation_without_a_region()
    {
        var settings = AwsSettings();
        settings.Remove("Storage:S3:Region");
        settings["Storage:S3:ServiceUrl"] = "http://127.0.0.1:9000";
        settings["Storage:S3:ForcePathStyle"] = "true";
        using var services = StorageScratch.Build(settings);

        Assert.DoesNotThrow(() => services.GetRequiredService<IStartupValidator>().Validate());
    }

    [Test]
    public void Startup_fails_when_the_bucket_is_missing()
    {
        var settings = AwsSettings();
        settings.Remove("Storage:S3:Bucket");

        Assert.That(StartupFailure(settings), Does.Contain("Storage:S3:Bucket is required"));
    }

    [TestCase("ab", TestName = "A_bucket_name_that_is_too_short_is_rejected")]
    [TestCase("Upper-Case", TestName = "A_bucket_name_with_upper_case_is_rejected")]
    [TestCase("bucket_name", TestName = "A_bucket_name_with_an_underscore_is_rejected")]
    [TestCase("-bucket", TestName = "A_bucket_name_starting_with_a_hyphen_is_rejected")]
    [TestCase("bucket/documents", TestName = "A_bucket_name_with_a_slash_is_rejected")]
    public void Startup_fails_with_an_invalid_bucket_name(string bucket)
    {
        var settings = AwsSettings();
        settings["Storage:S3:Bucket"] = bucket;

        Assert.That(StartupFailure(settings), Does.Contain("is not a valid S3 bucket name"));
    }

    [Test]
    public void Startup_fails_without_a_region_when_no_service_url_is_set()
    {
        var settings = AwsSettings();
        settings.Remove("Storage:S3:Region");

        Assert.That(StartupFailure(settings), Does.Contain("Storage:S3:Region is required"));
    }

    [Test]
    public void Startup_fails_with_a_malformed_region()
    {
        var settings = AwsSettings();
        settings["Storage:S3:Region"] = "Virginia";

        Assert.That(StartupFailure(settings), Does.Contain("is not a valid AWS region"));
    }

    [TestCase("minio:9000", TestName = "A_service_url_without_a_scheme_is_rejected")]
    [TestCase("/relative/path", TestName = "A_relative_service_url_is_rejected")]
    [TestCase("ftp://127.0.0.1:9000", TestName = "A_service_url_that_is_not_http_is_rejected")]
    [TestCase("http://user:secret@127.0.0.1:9000", TestName = "A_service_url_with_credentials_is_rejected")]
    [TestCase("http://127.0.0.1:9000/?bucket=x", TestName = "A_service_url_with_a_query_is_rejected")]
    public void Startup_fails_with_a_bad_service_url(string serviceUrl)
    {
        var settings = AwsSettings();
        settings["Storage:S3:ServiceUrl"] = serviceUrl;

        Assert.That(StartupFailure(settings), Does.Contain("Storage:S3:ServiceUrl"));
    }

    [TestCase("0")]
    [TestCase("10081")]
    public void Startup_fails_when_the_signed_url_limit_is_out_of_range(string minutes)
    {
        var settings = AwsSettings();
        settings["Storage:S3:SignedUrlMaxMinutes"] = minutes;

        Assert.That(StartupFailure(settings), Does.Contain("Storage:S3:SignedUrlMaxMinutes"));
    }

    private static Dictionary<string, string?> AwsSettings() => new()
    {
        ["Storage:Provider"] = "S3",
        ["Storage:S3:Bucket"] = "impifact-documents",
        ["Storage:S3:Region"] = "us-east-1",
    };

    private static string StartupFailure(Dictionary<string, string?> settings)
    {
        using var services = StorageScratch.Build(settings);
        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());
        return exception!.Message;
    }
}
