using ImpiFact.Application.Abstractions;
using ImpiFact.Infrastructure;
using ImpiFact.Infrastructure.Identity;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Infrastructure.Tenancy;
using ImpiFact.Integration.Tests.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.IdentityData;

/// <summary>
/// PostgreSQL desechable en Docker para las pruebas de Identity sobre las tablas reales: el esquema sale de las
/// migraciones reales y los catálogos del sincronizador, como en un despliegue. Es un contenedor propio porque un
/// <c>SetUpFixture</c> de NUnit solo cubre su espacio de nombres.
/// </summary>
[SetUpFixture]
[Category("Integration")]
public sealed class IdentityDatabase
{
    private static TestDatabaseServer? container;
    private static ServiceProvider? services;

    public static string ConnectionString =>
        container?.GetConnectionString() ?? throw new InvalidOperationException("The PostgreSQL container is not running.");

    public static IServiceProvider Services => services ?? throw new InvalidOperationException("The identity services are not built.");

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
                ["ApiKey:Prefix"] = "apk_",
            })
            .Build();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddInfrastructure(configuration, TestDatabase.Providers);
        collection.AddIdentityPersistence();
        services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var scope = Open(companyId: null);
        var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
        await db.Database.MigrateAsync();
        await new CatalogSynchronizer(db).SynchronizeAsync(CancellationToken.None);
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
    }

    /// <summary>Un ámbito («solicitud») sobre la empresa indicada; sin empresa actúa como proceso del sistema.</summary>
    public static AsyncServiceScope Open(Guid? companyId)
    {
        var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>()
            .Set(companyId, userId: null, companyId is null ? ActorType.System : ActorType.ApiKey);
        return scope;
    }

    /// <summary>Crea una empresa mínima, como haría el módulo Company, y devuelve su id.</summary>
    public static async Task<Guid> AddCompanyAsync()
    {
        var id = Guid.CreateVersion7();
        await using var scope = Open(companyId: null);
        var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
        db.Add(ModelGraph.NewCompany(id, Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        await db.SaveChangesAsync();
        return id;
    }
}
