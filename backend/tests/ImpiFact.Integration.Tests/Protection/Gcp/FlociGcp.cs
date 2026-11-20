using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Google.Api.Gax.ResourceNames;
using Google.Cloud.Kms.V1;
using Google.Cloud.SecretManager.V1;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Protection.Gcp;

/// <summary>
/// floci-gcp (un emulador de código abierto de servicios de Google Cloud, licencia MIT) desechable en Docker para probar
/// el proveedor <c>GcpSecrets</c> contra Secret Manager y Cloud KMS reales del emulador, y una base del motor de
/// <see cref="TestDatabase"/> para las llaves de Data Protection. Guarda todo en memoria y no pide credenciales: la
/// aplicación llega por <c>Secrets:GcpSecrets:Endpoint</c>, la misma ruta que con el perfil <c>gcp</c> del compose. El
/// anillo y las llaves de Cloud KMS los crea esta clase, como lo haría el despliegue.
/// </summary>
[SetUpFixture]
[Category("Integration")]
public sealed class FlociGcp
{
    public const string Image = "floci/floci-gcp:0.13.0";

    public const string ProjectId = "impifact-tests";

    private const int Port = 4588;

    private static IContainer? container;
    private static TestDatabaseServer? database;

    public static string Endpoint { get; private set; } = "";

    public static string KeyName { get; private set; } = "";

    public static string OtherKeyName { get; private set; } = "";

    /// <summary>Cliente directo de Secret Manager del emulador, para revisar lo que la aplicación guardó.</summary>
    public static SecretManagerServiceClient SecretsClient { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        container = new ContainerBuilder(Image)
            .WithPortBinding(Port, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request =>
                request.ForPort(Port).ForPath($"/v1/projects/{ProjectId}/secrets").ForStatusCodeMatching(_ => true)))
            .Build();
        database = TestDatabase.CreateServer();
        await Task.WhenAll(container.StartAsync(), database.StartAsync());

        Endpoint = $"http://127.0.0.1:{container.GetMappedPublicPort(Port)}";
        var grpcEndpoint = $"127.0.0.1:{container.GetMappedPublicPort(Port)}";
        SecretsClient = await new SecretManagerServiceClientBuilder { Endpoint = grpcEndpoint, ChannelCredentials = ChannelCredentials.Insecure }.BuildAsync();
        var kms = await new KeyManagementServiceClientBuilder { Endpoint = grpcEndpoint, ChannelCredentials = ChannelCredentials.Insecure }.BuildAsync();
        var ring = await kms.CreateKeyRingAsync(new LocationName(ProjectId, "global"), "impifact", new KeyRing());
        KeyName = (await kms.CreateCryptoKeyAsync(ring.KeyRingName, "data-protection", new CryptoKey { Purpose = CryptoKey.Types.CryptoKeyPurpose.EncryptDecrypt })).Name;
        OtherKeyName = (await kms.CreateCryptoKeyAsync(ring.KeyRingName, "data-protection-other", new CryptoKey { Purpose = CryptoKey.Types.CryptoKeyPurpose.EncryptDecrypt })).Name;
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }

        if (database is not null)
        {
            await database.DisposeAsync();
        }
    }

    /// <summary>Configuración del proveedor <c>GcpSecrets</c> contra el emulador.</summary>
    public static Dictionary<string, string?> Settings(string? keyName = null) => new()
    {
        ["Secrets:Provider"] = "GcpSecrets",
        ["Secrets:GcpSecrets:ProjectId"] = ProjectId,
        ["Secrets:GcpSecrets:KmsKeyName"] = keyName ?? KeyName,
        ["Secrets:GcpSecrets:Endpoint"] = Endpoint,
    };

    /// <summary>Servicios como los de la API con el proveedor <c>GcpSecrets</c>, y una base propia si se pasa su cadena.</summary>
    public static ServiceProvider Build(Dictionary<string, string?>? secrets = null, string? connectionString = null) =>
        ProtectionServices.Build(ProtectionServices.ValidSettings(secrets ?? Settings(), connectionString));

    /// <summary>Una base nueva con la tabla <c>data_protection_keys</c>, para las llaves de una prueba.</summary>
    public static Task<string> CreateKeysDatabaseAsync() =>
        CloudKeysDatabase.CreateAsync(database ?? throw new InvalidOperationException("The database server is not running."));
}
