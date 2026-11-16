using ImpiFact.Application.Abstractions;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Integration.Tests.Companies;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ImpiFact.Integration.Tests.DocumentQueries;

/// <summary>
/// Levanta un PostgreSQL desechable en Docker y la API real encima, una sola vez para las pruebas de este espacio de
/// nombres (un <c>SetUpFixture</c> de NUnit solo cubre su propio espacio de nombres). El esquema sale de las
/// migraciones reales y del sincronizador de catálogos; la API es la misma fábrica de las pruebas de Company.
/// </summary>
[SetUpFixture]
[Category("Integration")]
public sealed class DocumentQueriesHost
{
    private static PostgreSqlContainer? container;
    private static CompanyApiFactory? factory;

    public static CompanyApiFactory Api => factory ?? throw new InvalidOperationException("The document query API host is not running.");

    public static string ConnectionString =>
        container?.GetConnectionString() ?? throw new InvalidOperationException("The PostgreSQL container is not running.");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        container = new PostgreSqlBuilder("postgres:18.1-alpine").Build();
        await container.StartAsync();
        factory = new CompanyApiFactory(container.GetConnectionString());

        await using var scope = factory.OpenScope(companyId: null, userId: null, ActorType.System);
        await scope.Db.Database.MigrateAsync();
        await new CatalogSynchronizer(scope.Db).SynchronizeAsync(CancellationToken.None);
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }
}
