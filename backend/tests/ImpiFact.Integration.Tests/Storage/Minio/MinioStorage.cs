using System.Globalization;
using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using ImpiFact.Application;
using ImpiFact.Application.Abstractions;
using ImpiFact.Infrastructure;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Infrastructure.Storage;
using ImpiFact.Infrastructure.Tenancy;
using ImpiFact.Integration.Tests.Documents;
using ImpiFact.Integration.Tests.Model;
using ImpiFact.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;

namespace ImpiFact.Integration.Tests.Storage.Minio;

/// <summary>
/// MinIO (un servidor de almacenamiento de objetos compatible con S3, el servicio de Amazon) y PostgreSQL
/// desechables en Docker para probar el proveedor S3 contra un servicio real. MinIO arranca con una llave de cifrado
/// propia generada en cada corrida (<c>MINIO_KMS_SECRET_KEY</c>), sin la cual no acepta el cifrado del lado del
/// servidor (SSE-S3); las credenciales de MinIO llegan a la aplicación por inyección de prueba, nunca por
/// configuración.
/// </summary>
/// <remarks>
/// Es un contenedor propio porque un <c>SetUpFixture</c> de NUnit solo cubre su espacio de nombres. La imagen es la
/// del fork comunitario de MinIO, porque el proyecto dejó de publicar imágenes oficiales en 2025.
/// </remarks>
[SetUpFixture]
[Category("Integration")]
public sealed class MinioStorage
{
    public const string Image = "pgsty/minio:RELEASE.2026-08-04T00-00-00Z";

    public const string Bucket = "impifact-tests";

    private static MinioContainer? minio;
    private static PostgreSqlContainer? postgres;
    private static ServiceProvider? services;
    private static AmazonS3Client? client;
    private static int nitSequence;

    public static string ServiceUrl => minio?.GetConnectionString() ?? throw new InvalidOperationException("The MinIO container is not running.");

    /// <summary>Cliente S3 directo, con las credenciales de MinIO, para revisar lo que la aplicación guardó.</summary>
    public static AmazonS3Client Client => client ?? throw new InvalidOperationException("The MinIO container is not running.");

    /// <summary>Credenciales de MinIO para armar otros almacenes en una prueba.</summary>
    public static AWSCredentials Credentials => new BasicAWSCredentials(minio!.GetAccessKey(), minio.GetSecretKey());

    /// <summary>Servicios reales de Application e Infrastructure con el almacén S3 contra MinIO.</summary>
    public static IServiceProvider Services => services ?? throw new InvalidOperationException("The MinIO services are not built.");

    public static Dictionary<string, string?> StorageSettings() => new()
    {
        ["Storage:Provider"] = "S3",
        ["Storage:S3:Bucket"] = Bucket,
        ["Storage:S3:Region"] = "us-east-1",
        ["Storage:S3:ServiceUrl"] = ServiceUrl,
        ["Storage:S3:ForcePathStyle"] = "true",
    };

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        // Llave de cifrado de MinIO solo para esta corrida: «nombre:base64 de 32 bytes».
        var kmsKey = "impifact-tests:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        minio = new MinioBuilder(Image).WithEnvironment("MINIO_KMS_SECRET_KEY", kmsKey).Build();
        postgres = new PostgreSqlBuilder("postgres:18.1-alpine").Build();
        await Task.WhenAll(minio.StartAsync(), postgres.StartAsync());

        client = new AmazonS3Client(Credentials, new AmazonS3Config { ServiceURL = ServiceUrl, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        await client.PutBucketAsync(Bucket);

        var settings = StorageSettings();
        settings["Database:Provider"] = TestDatabase.ProviderName;
        settings["Database:ConnectionString"] = postgres.GetConnectionString();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddInfrastructure(configuration, TestDatabase.Providers);
        collection.AddApplication();
        collection.AddSingleton<IClock>(new FixedClock(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero)));
        collection.AddSingleton(new S3Credentials(Credentials));
        services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var session = Open(companyId: null);
        await session.Db.Database.MigrateAsync();
        await new CatalogSynchronizer(session.Db).SynchronizeAsync(CancellationToken.None);
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (services is not null)
        {
            await services.DisposeAsync();
        }

        client?.Dispose();
        if (minio is not null)
        {
            await minio.DisposeAsync();
        }

        if (postgres is not null)
        {
            await postgres.DisposeAsync();
        }
    }

    /// <summary>Arma un almacén aparte con la configuración indicada (por ejemplo, sin cifrado del lado del servidor).</summary>
    public static ServiceProvider BuildStorage(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var collection = new ServiceCollection();
        collection.AddValidatedOptions<StorageOptions>(configuration, StorageOptions.SectionName);
        collection.AddSingleton(new S3Credentials(Credentials));
        collection.AddStorage();
        return collection.BuildServiceProvider();
    }

    /// <summary>Abre un ámbito; con empresa actúa como su API key, sin empresa como proceso del sistema.</summary>
    public static DocumentsSession Open(Guid? companyId)
    {
        var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>()
            .Set(companyId, userId: null, companyId is null ? ActorType.System : ActorType.ApiKey);
        return new DocumentsSession(scope);
    }

    /// <summary>Crea y guarda una empresa nueva, sin filas hijas.</summary>
    public static async Task<Guid> CreateCompanyAsync()
    {
        var id = Guid.CreateVersion7();
        var nit = (810000000 + Interlocked.Increment(ref nitSequence)).ToString(CultureInfo.InvariantCulture);
        await using var session = Open(companyId: null);
        session.Db.Add(ModelGraph.NewCompany(id, nit));
        await session.Db.SaveChangesAsync();
        return id;
    }
}
