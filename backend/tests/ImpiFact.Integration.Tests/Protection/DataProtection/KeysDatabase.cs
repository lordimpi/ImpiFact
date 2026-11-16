using ImpiFact.Application.Abstractions;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ImpiFact.Integration.Tests.Protection.DataProtection;

/// <summary>
/// Levanta un PostgreSQL desechable en Docker para las pruebas de Data Protection (el sistema de ASP.NET Core
/// que cifra datos con llaves que él mismo crea y rota). Cada prueba pide su propia base vacía con
/// <see cref="CreateDatabaseAsync"/>, así las llaves de una prueba no se mezclan con las de otra.
/// </summary>
[SetUpFixture]
[Category("Integration")]
public sealed class KeysDatabase
{
    private static PostgreSqlContainer? container;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        container = new PostgreSqlBuilder("postgres:18.1-alpine").Build();
        await container.StartAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    /// <summary>
    /// Crea una base nueva con la tabla <c>data_protection_keys</c> tal como la mapea el contexto de producción,
    /// y devuelve su cadena de conexión.
    /// </summary>
    public static async Task<string> CreateDatabaseAsync()
    {
        var admin = container?.GetConnectionString() ?? throw new InvalidOperationException("The PostgreSQL container is not running.");
        var name = "keys_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"create database {name}", connection);
            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = name }.ConnectionString;
        var options = new DbContextOptionsBuilder<KeysOnlyDbContext>().UseNpgsql(connectionString).Options;
        await using var context = new KeysOnlyDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return connectionString;
    }
}

/// <summary>
/// El contexto de producción sin las entidades de negocio: solo queda <c>data_protection_keys</c>, que el
/// contexto base mapea siempre. Sirve para crear esa tabla y nada más.
/// </summary>
public sealed class KeysOnlyDbContext(DbContextOptions<KeysOnlyDbContext> options)
    : ImpiFactDbContext(options, new NoClock(), new NoTenant())
{
    protected override void ConfigureEntities(ModelBuilder modelBuilder)
    {
    }

    private sealed class NoClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid? CompanyId => null;

        public Guid? UserId => null;

        public ActorType Actor => ActorType.System;

        public IReadOnlySet<string> Scopes { get; } = new HashSet<string>();
    }
}
