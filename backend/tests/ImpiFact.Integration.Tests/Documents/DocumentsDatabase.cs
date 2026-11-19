using System.Globalization;
using ImpiFact.Application;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Infrastructure;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Infrastructure.Tenancy;
using ImpiFact.Integration.Tests.Model;
using ImpiFact.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Documents;

/// <summary>
/// PostgreSQL desechable en Docker para las piezas de emisión de documentos (consecutivos, bandeja de salida y
/// archivos), con el esquema de las migraciones reales y los catálogos del sincronizador. Arma los servicios reales
/// de Application e Infrastructure, con el almacén local de archivos en una carpeta temporal fuera del repositorio.
/// </summary>
/// <remarks>
/// Es un contenedor propio porque un <c>SetUpFixture</c> de NUnit solo cubre su espacio de nombres.
/// </remarks>
[SetUpFixture]
[Category("Integration")]
public sealed class DocumentsDatabase
{
    private static TestDatabaseServer? container;
    private static ServiceProvider? services;
    private static int nitSequence;

    public static FixedClock Clock { get; } = new(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));

    /// <summary>Raíz del almacén local de archivos de estas pruebas.</summary>
    public static string StorageRoot { get; } =
        Path.Combine(Path.GetTempPath(), "impifact-tests", Guid.NewGuid().ToString("N"), "storage");

    public static string ConnectionString =>
        container?.GetConnectionString() ?? throw new InvalidOperationException("The PostgreSQL container is not running.");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        container = TestDatabase.CreateServer();
        await container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = TestDatabase.ProviderName,
                ["Database:ConnectionString"] = ConnectionString,
                ["Storage:Provider"] = "Local",
                ["Storage:Local:Root"] = StorageRoot,
            })
            .Build();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddInfrastructure(configuration, TestDatabase.Providers);
        collection.AddApplication();
        collection.AddSingleton<IClock>(Clock);
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

        if (container is not null)
        {
            await container.DisposeAsync();
        }

        var scratch = Path.GetDirectoryName(StorageRoot)!;
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    /// <summary>
    /// Abre un ámbito (una «solicitud»). Con empresa actúa como su API key; sin empresa, como proceso del sistema,
    /// que puede escribir para cualquier empresa. <paramref name="actor"/> permite probar otros actores.
    /// </summary>
    public static DocumentsSession Open(Guid? companyId, ActorType? actor = null)
    {
        var scope = (services ?? throw new InvalidOperationException("The documents services are not built.")).CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>()
            .Set(companyId, userId: null, actor ?? (companyId is null ? ActorType.System : ActorType.ApiKey));
        return new DocumentsSession(scope);
    }

    /// <summary>Crea y guarda una empresa nueva, sin filas hijas.</summary>
    public static async Task<Guid> CreateCompanyAsync()
    {
        var id = Guid.CreateVersion7();
        var nit = (800000000 + Interlocked.Increment(ref nitSequence)).ToString(CultureInfo.InvariantCulture);
        await using var session = Open(companyId: null);
        session.Db.Add(ModelGraph.NewCompany(id, nit));
        await session.Db.SaveChangesAsync();
        return id;
    }
}

/// <summary>Un ámbito de servicios con su contexto de producción.</summary>
public sealed class DocumentsSession(AsyncServiceScope scope) : IAsyncDisposable
{
    public IServiceProvider Services => scope.ServiceProvider;

    public ImpiFactDbContext Db { get; } = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();

    public IUnitOfWork UnitOfWork => scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

    public T Get<T>()
        where T : notnull => scope.ServiceProvider.GetRequiredService<T>();

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}
