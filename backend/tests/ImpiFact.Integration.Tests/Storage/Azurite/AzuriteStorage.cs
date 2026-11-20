using Azure.Storage.Blobs;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Azurite;

namespace ImpiFact.Integration.Tests.Storage.Azurite;

/// <summary>
/// Azurite (el emulador de Azure Storage que publica Microsoft) desechable en Docker para probar el proveedor
/// <c>AzureBlob</c> contra un servicio real. Guarda todo en memoria. La cuenta es la de desarrollo que trae Azurite
/// (<c>devstoreaccount1</c>, con su llave pública y documentada); llega a la aplicación por inyección de prueba
/// (<see cref="AzureBlobServiceClient"/>), nunca por configuración: en producción el cliente usa la identidad de la
/// carga de trabajo.
/// </summary>
[SetUpFixture]
[Category("Integration")]
public sealed class AzuriteStorage
{
    public const string Image = "mcr.microsoft.com/azure-storage/azurite:3.38.0";

    public const string Container = "impifact-tests";

    private static AzuriteContainer? azurite;
    private static ServiceProvider? services;

    /// <summary>Cliente directo de la cuenta del emulador, para revisar lo que la aplicación guardó.</summary>
    public static BlobServiceClient Client { get; private set; } = null!;

    public static string ServiceUri => azurite?.GetBlobEndpoint() ?? throw new InvalidOperationException("The Azurite container is not running.");

    /// <summary>Servicios del almacenamiento con el proveedor <c>AzureBlob</c> contra Azurite.</summary>
    public static IServiceProvider Services => services ?? throw new InvalidOperationException("The Azurite services are not built.");

    public static Dictionary<string, string?> StorageSettings(string container = Container) => new()
    {
        ["Storage:Provider"] = "AzureBlob",
        ["Storage:AzureBlob:ServiceUri"] = ServiceUri,
        ["Storage:AzureBlob:Container"] = container,
    };

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        azurite = new AzuriteBuilder(Image).WithInMemoryPersistence().Build();
        await azurite.StartAsync();

        Client = new BlobServiceClient(azurite.GetConnectionString());
        await Client.CreateBlobContainerAsync(Container);
        services = BuildStorage(StorageSettings());
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (services is not null)
        {
            await services.DisposeAsync();
        }

        if (azurite is not null)
        {
            await azurite.DisposeAsync();
        }
    }

    /// <summary>Arma un almacén aparte con la configuración indicada (por ejemplo, con un contenedor que no existe).</summary>
    public static ServiceProvider BuildStorage(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var collection = new ServiceCollection();
        collection.AddValidatedOptions<StorageOptions>(configuration, StorageOptions.SectionName);
        collection.AddSingleton(new AzureBlobServiceClient(Client));
        collection.AddStorage();
        return collection.BuildServiceProvider();
    }
}
