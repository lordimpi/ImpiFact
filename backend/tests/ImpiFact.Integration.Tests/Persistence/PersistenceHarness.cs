using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Infrastructure;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Postgres;
using ImpiFact.Infrastructure.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Persistence;

/// <summary>
/// Arma el contenedor de dependencias igual que la API (<c>AddInfrastructure</c> con el proveedor de
/// PostgreSQL), cambia el reloj por uno fijo y registra el contexto de prueba con la misma configuración que el
/// de producción.
/// </summary>
public sealed class PersistenceHarness : IDisposable
{
    private readonly ServiceProvider services;

    public PersistenceHarness()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Postgres",
                ["Database:ConnectionString"] = PostgresDatabase.ConnectionString,
            })
            .Build();

        var collection = new ServiceCollection();
        collection.AddInfrastructure(configuration, new PostgresDatabaseProvider());
        collection.AddSingleton<IClock>(Clock);
        collection.AddImpiFactDbContext<TestDbContext>();
        services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public FixedClock Clock { get; } = new(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));

    /// <summary>Abre un ámbito (una «solicitud») que actúa sobre la empresa indicada.</summary>
    public Session Open(Guid? companyId) => Open(companyId, companyId is null ? ActorType.System : ActorType.ApiKey);

    /// <summary>Abre un ámbito con un tipo de actor concreto, por ejemplo una API key de plataforma.</summary>
    public Session Open(Guid? companyId, ActorType actor)
    {
        var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(companyId, userId: null, actor);
        return new Session(scope);
    }

    public void Dispose() => services.Dispose();
}

/// <summary>Las piezas de persistencia de un ámbito.</summary>
public sealed class Session(AsyncServiceScope scope) : IAsyncDisposable
{
    public TestDbContext Db { get; } = scope.ServiceProvider.GetRequiredService<TestDbContext>();

    public IUnitOfWork UnitOfWork { get; } = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

    public IReadConnectionFactory Reads { get; } = scope.ServiceProvider.GetRequiredService<IReadConnectionFactory>();

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}
