using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Storage.FakeGcs;

/// <summary>
/// fake-gcs-server (un emulador de código abierto de la API JSON de Google Cloud Storage) desechable en Docker para
/// probar el proveedor <c>Gcs</c> contra un servicio real. Guarda todo en memoria y no pide credenciales: la aplicación
/// llega por <c>Storage:Gcs:Endpoint</c>, la misma ruta que tendría un desarrollador con el perfil <c>gcs</c> del
/// compose.
/// </summary>
/// <remarks>
/// El emulador devuelve en la cabecera <c>Location</c> de cada subida la dirección pública que le pasan al arrancar
/// (<c>-external-url</c>), así que el puerto del equipo se elige antes de crearlo y no al azar después. Las URL
/// firmadas se firman con una cuenta de servicio generada en la prueba (<see cref="Signer"/>): el emulador no revisa
/// firmas, así que se revisa la forma de la URL.
/// </remarks>
[SetUpFixture]
[Category("Integration")]
public sealed class FakeGcsStorage
{
    public const string Image = "fsouza/fake-gcs-server:1.56.1";

    public const string Bucket = "impifact-tests";

    public const string SignerEmail = "impifact-signer@impifact-tests.iam.gserviceaccount.com";

    private const int ContainerPort = 4443;

    private static IContainer? container;
    private static ServiceProvider? services;
    private static RSA? signingKey;

    public static string Endpoint { get; private set; } = "";

    /// <summary>Cliente directo del emulador, para revisar lo que la aplicación guardó.</summary>
    public static StorageClient Client { get; private set; } = null!;

    /// <summary>Firmante con una cuenta de servicio y una llave RSA generadas en esta corrida.</summary>
    public static UrlSigner Signer { get; private set; } = null!;

    /// <summary>Servicios del almacenamiento con el proveedor <c>Gcs</c> contra el emulador.</summary>
    public static IServiceProvider Services => services ?? throw new InvalidOperationException("The fake-gcs-server services are not built.");

    public static Dictionary<string, string?> StorageSettings(string bucket = Bucket) => new()
    {
        ["Storage:Provider"] = "Gcs",
        ["Storage:Gcs:Bucket"] = bucket,
        ["Storage:Gcs:Endpoint"] = Endpoint,
    };

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        var hostPort = FreePort();
        Endpoint = $"http://127.0.0.1:{hostPort}";
        container = new ContainerBuilder(Image)
            .WithCommand("-scheme", "http", "-port", ContainerPort.ToString(System.Globalization.CultureInfo.InvariantCulture), "-backend", "memory", "-external-url", Endpoint)
            .WithPortBinding(hostPort, ContainerPort)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(ContainerPort).ForPath("/storage/v1/b")))
            .Build();
        await container.StartAsync();

        Client = await new StorageClientBuilder { BaseUri = Endpoint + "/storage/v1/", UnauthenticatedAccess = true }.BuildAsync();
        await Client.CreateBucketAsync("impifact-tests", Bucket);

        signingKey = RSA.Create(2048);
        var credential = new ServiceAccountCredential(
            new ServiceAccountCredential.Initializer(SignerEmail).FromPrivateKey(signingKey.ExportPkcs8PrivateKeyPem()));
        Signer = UrlSigner.FromCredential(credential);
        services = BuildStorage(StorageSettings());
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (services is not null)
        {
            await services.DisposeAsync();
        }

        Client?.Dispose();
        signingKey?.Dispose();
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    /// <summary>Arma un almacén aparte con la configuración indicada (por ejemplo, con un bucket que no existe).</summary>
    public static ServiceProvider BuildStorage(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var collection = new ServiceCollection();
        collection.AddValidatedOptions<StorageOptions>(configuration, StorageOptions.SectionName);
        collection.AddSingleton(new GcsUrlSigner(Signer));
        collection.AddStorage();
        return collection.BuildServiceProvider();
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
