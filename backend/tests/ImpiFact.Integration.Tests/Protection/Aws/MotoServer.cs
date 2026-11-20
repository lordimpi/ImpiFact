using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime;
using Amazon.SecretsManager;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using ImpiFact.Infrastructure.Protection;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Protection.Aws;

/// <summary>
/// Moto (un emulador de código abierto de los servicios de AWS, licencia Apache-2.0) desechable en Docker, en modo
/// servidor, para probar el proveedor <c>AwsSecrets</c> contra Secrets Manager y KMS reales del emulador, y una base del
/// motor de <see cref="TestDatabase"/> para las llaves de Data Protection. Guarda todo en memoria; las llaves de KMS las
/// crea esta clase, como lo haría el despliegue.
/// </summary>
/// <remarks>
/// Moto acepta cualquier credencial, así que las pruebas usan unas fijas sin valor (<see cref="Credentials"/>), que llegan
/// a la aplicación por <see cref="AwsSecretsCredentials"/>, nunca por configuración. Se eligió Moto y no LocalStack porque
/// LocalStack exige una cuenta y un token desde marzo de 2026.
/// </remarks>
[SetUpFixture]
[Category("Integration")]
public sealed class MotoServer
{
    public const string Image = "motoserver/moto:5.2.5";

    public const string KeyAlias = "alias/impifact-data-protection";

    public const string OtherKeyAlias = "alias/impifact-data-protection-other";

    private const int Port = 5000;

    private static IContainer? container;
    private static TestDatabaseServer? database;

    public static string ServiceUrl { get; private set; } = "";

    public static AWSCredentials Credentials { get; } = new BasicAWSCredentials("moto-test-access-key", "moto-test-secret-key");

    /// <summary>Cliente directo de Secrets Manager del emulador, para revisar lo que la aplicación guardó.</summary>
    public static AmazonSecretsManagerClient SecretsClient { get; private set; } = null!;

    /// <summary>ARN de la llave de <see cref="KeyAlias"/>.</summary>
    public static string KeyArn { get; private set; } = "";

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        container = new ContainerBuilder(Image)
            .WithPortBinding(Port, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(Port).ForPath("/moto-api/")))
            .Build();
        database = TestDatabase.CreateServer();
        await Task.WhenAll(container.StartAsync(), database.StartAsync());

        ServiceUrl = $"http://127.0.0.1:{container.GetMappedPublicPort(Port)}";
        SecretsClient = new AmazonSecretsManagerClient(Credentials, new AmazonSecretsManagerConfig { ServiceURL = ServiceUrl, AuthenticationRegion = "us-east-1" });
        using var kms = new AmazonKeyManagementServiceClient(Credentials, new AmazonKeyManagementServiceConfig { ServiceURL = ServiceUrl, AuthenticationRegion = "us-east-1" });
        foreach (var alias in new[] { KeyAlias, OtherKeyAlias })
        {
            var key = await kms.CreateKeyAsync(new CreateKeyRequest { Description = "ImpiFact tests: " + alias });
            await kms.CreateAliasAsync(new CreateAliasRequest { AliasName = alias, TargetKeyId = key.KeyMetadata.KeyId });
            if (alias == KeyAlias)
            {
                KeyArn = key.KeyMetadata.Arn;
            }
        }
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        SecretsClient?.Dispose();
        if (container is not null)
        {
            await container.DisposeAsync();
        }

        if (database is not null)
        {
            await database.DisposeAsync();
        }
    }

    /// <summary>Configuración del proveedor <c>AwsSecrets</c> contra el emulador.</summary>
    public static Dictionary<string, string?> Settings(string kmsKeyId = KeyAlias) => new()
    {
        ["Secrets:Provider"] = "AwsSecrets",
        ["Secrets:AwsSecrets:ServiceUrl"] = ServiceUrl,
        ["Secrets:AwsSecrets:KmsKeyId"] = kmsKeyId,
    };

    /// <summary>Servicios como los de la API con el proveedor <c>AwsSecrets</c>, y una base propia si se pasa su cadena.</summary>
    public static ServiceProvider Build(Dictionary<string, string?>? secrets = null, string? connectionString = null) =>
        ProtectionServices.Build(
            ProtectionServices.ValidSettings(secrets ?? Settings(), connectionString),
            extra: services => services.AddSingleton(new AwsSecretsCredentials(Credentials)));

    /// <summary>Una base nueva con la tabla <c>data_protection_keys</c>, para las llaves de una prueba.</summary>
    public static Task<string> CreateKeysDatabaseAsync() =>
        CloudKeysDatabase.CreateAsync(database ?? throw new InvalidOperationException("The database server is not running."));
}
