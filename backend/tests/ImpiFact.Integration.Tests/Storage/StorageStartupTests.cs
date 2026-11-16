using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ImpiFact.Integration.Tests.Storage;

/// <summary>
/// El almacenamiento de archivos se valida al arrancar (Arquitectura detallada, sección 4.3): con el proveedor
/// <c>Local</c>, la aplicación no arranca sin una carpeta raíz absoluta que se pueda escribir. No necesitan Docker.
/// </summary>
[TestFixture]
public sealed class StorageStartupTests
{
    private StorageScratch scratch = null!;

    [SetUp]
    public void CreateScratch() => scratch = new StorageScratch();

    [TearDown]
    public void DeleteScratch() => scratch.Dispose();

    [Test]
    public void A_writable_absolute_root_passes_startup_validation_and_is_created()
    {
        using var services = StorageScratch.Build(scratch.LocalSettings());

        Assert.DoesNotThrow(() => services.GetRequiredService<IStartupValidator>().Validate());
        Assert.That(Directory.Exists(scratch.StorageRoot), Is.True);
    }

    [Test]
    public void Startup_fails_without_a_provider()
    {
        Assert.That(StartupFailure(new() { ["Storage:Local:Root"] = scratch.StorageRoot }), Does.Contain("Provider"));
    }

    [Test]
    public void Startup_fails_when_the_local_root_is_not_configured()
    {
        var settings = scratch.LocalSettings();
        settings.Remove("Storage:Local:Root");

        Assert.That(StartupFailure(settings), Does.Contain("Storage:Local:Root is required"));
    }

    [Test]
    public void Startup_fails_when_the_local_root_is_relative()
    {
        var settings = scratch.LocalSettings();
        settings["Storage:Local:Root"] = "data/files";

        Assert.That(StartupFailure(settings), Does.Contain("must be an absolute path"));
    }

    // Una carpeta que no se puede crear (aquí, porque ya hay un archivo con ese nombre) debe fallar al arrancar, no al
    // guardar el primer XML.
    [Test]
    public void Startup_fails_when_the_local_root_cannot_be_created()
    {
        var blocked = Path.Combine(scratch.Root, "not-a-directory");
        File.WriteAllText(blocked, "a file, not a folder");
        var settings = scratch.LocalSettings();
        settings["Storage:Local:Root"] = Path.Combine(blocked, "storage");

        Assert.That(StartupFailure(settings), Does.Contain("cannot be created or written"));
    }

    [TestCase("AzureBlob")]
    [TestCase("S3")]
    [TestCase("Gcs")]
    public void Startup_fails_with_a_cloud_provider_that_is_not_available_yet(string provider)
    {
        var settings = scratch.LocalSettings();
        settings["Storage:Provider"] = provider;

        Assert.That(StartupFailure(settings), Does.Contain($"Storage provider '{provider}' is not available yet"));
    }

    [Test]
    public void Startup_fails_with_an_unknown_provider()
    {
        var settings = scratch.LocalSettings();
        settings["Storage:Provider"] = "Ftp";

        Assert.That(StartupFailure(settings), Does.Contain("Provider"));
    }

    private static string StartupFailure(Dictionary<string, string?> settings)
    {
        using var services = StorageScratch.Build(settings);
        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());
        return exception!.Message;
    }
}
