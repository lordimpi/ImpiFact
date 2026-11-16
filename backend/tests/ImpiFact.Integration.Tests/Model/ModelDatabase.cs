using ImpiFact.Application.Abstractions;
using ImpiFact.Infrastructure;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Postgres;
using ImpiFact.Infrastructure.Tenancy;
using ImpiFact.Integration.Tests.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace ImpiFact.Integration.Tests.Model;

/// <summary>
/// Levanta un PostgreSQL desechable en Docker para las pruebas del modelo real y crea su esquema con
/// <c>EnsureCreated</c> (tablas, índices, restricciones y semillas a partir del modelo, sin migraciones).
/// </summary>
/// <remarks>
/// Es un contenedor aparte del de <c>Persistence</c> porque un <c>SetUpFixture</c> de NUnit solo cubre su propio
/// espacio de nombres; usa la misma imagen y el mismo armado de dependencias (<c>AddInfrastructure</c> con el
/// proveedor de PostgreSQL), pero con el contexto de producción en lugar del contexto de prueba.
/// </remarks>
[SetUpFixture]
[Category("Integration")]
public sealed class ModelDatabase
{
    private static PostgreSqlContainer? container;
    private static ServiceProvider? services;

    public static string ConnectionString =>
        container?.GetConnectionString() ?? throw new InvalidOperationException("The PostgreSQL container is not running.");

    public static FixedClock Clock { get; } = new(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        container = new PostgreSqlBuilder("postgres:18.1-alpine").Build();
        await container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Postgres",
                ["Database:ConnectionString"] = ConnectionString,
            })
            .Build();
        var collection = new ServiceCollection();
        collection.AddInfrastructure(configuration, new PostgresDatabaseProvider());
        collection.AddSingleton<IClock>(Clock);
        services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var session = Open(companyId: null);
        await session.Db.Database.EnsureCreatedAsync();
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

    /// <summary>
    /// Abre un ámbito (una «solicitud») sobre la empresa indicada. Sin empresa actúa como proceso del sistema,
    /// que es quien puede escribir filas de varias empresas.
    /// </summary>
    public static ModelSession Open(Guid? companyId)
    {
        var scope = (services ?? throw new InvalidOperationException("The model services are not built.")).CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>()
            .Set(companyId, userId: null, companyId is null ? ActorType.System : ActorType.ApiKey);
        return new ModelSession(scope);
    }
}

/// <summary>El contexto de producción de un ámbito.</summary>
public sealed class ModelSession(AsyncServiceScope scope) : IAsyncDisposable
{
    public ImpiFactDbContext Db { get; } = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}
