using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Companies;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Integration.Tests.Companies;
using ImpiFact.Integration.Tests.DocumentIssue;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ImpiFact.Integration.Tests.Plans;

/// <summary>
/// PostgreSQL desechable en Docker y la API real encima para las rutas de planes (carpeta 09 de la colección Postman),
/// una sola vez para este espacio de nombres. El esquema sale de las migraciones reales y del sincronizador de catálogos.
/// </summary>
[SetUpFixture]
[Category("Integration")]
public sealed class PlansHost
{
    private static PostgreSqlContainer? container;
    private static CompanyApiFactory? factory;

    public static CompanyApiFactory Api => factory ?? throw new InvalidOperationException("The plans API host is not running.");

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

    /// <summary>Una empresa nueva creada por la API de Company, con la API key que devuelve su alta.</summary>
    public static async Task<Issuer> CreateCompanyAsync(HttpClient platform)
    {
        var (nit, dv) = CompaniesHost.NewNit();
        using var created = await platform.PostAsJsonAsync($"/api/ubl2.1/config/{nit}/{dv}", IssuerSetup.GoldenCompanyBody($"{nit}@example.com"));
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), await created.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var client = Api.ClientWith(json.RootElement.GetProperty("token").GetString()!);
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var companyId = (await scope.Db.Set<Company>().SingleAsync(row => row.IdentificationNumber == nit)).Id;
        return new Issuer(client, companyId, nit);
    }

    public static Task<HttpClient> PlatformAsync() => Api.ClientWithNewKeyAsync(null, ApiScopes.Platform);
}
