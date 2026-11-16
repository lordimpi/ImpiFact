using System.Globalization;
using ImpiFact.Application;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Infrastructure.Platform.Outbox;
using ImpiFact.Infrastructure.Postgres;
using ImpiFact.Infrastructure.Tenancy;
using ImpiFact.Integration.Tests.Model;
using ImpiFact.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace ImpiFact.Integration.Tests.Outbox;

/// <summary>
/// PostgreSQL desechable en Docker (con Testcontainers) para el procesador de la bandeja de salida (outbox: la tabla
/// <c>outbox_messages</c> con el trabajo pendiente del Worker), con el esquema de las migraciones reales y los
/// catálogos del sincronizador.
/// </summary>
/// <remarks>
/// Es un contenedor propio porque un <c>SetUpFixture</c> de NUnit solo cubre su espacio de nombres. Cada prueba arma
/// su propio <see cref="OutboxHost"/> con un tipo de mensaje único y su propio reloj, así los mensajes de una prueba
/// nunca los toma el procesador de otra.
/// </remarks>
[SetUpFixture]
[Category("Integration")]
public sealed class OutboxDatabase
{
    private static PostgreSqlContainer? container;
    private static int nitSequence;

    public static string ConnectionString =>
        container?.GetConnectionString() ?? throw new InvalidOperationException("The PostgreSQL container is not running.");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        container = new PostgreSqlBuilder("postgres:18.1-alpine").Build();
        await container.StartAsync();

        await using var host = OutboxHost.Create();
        await using var scope = host.OpenScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
        await db.Database.MigrateAsync();
        await new CatalogSynchronizer(db).SynchronizeAsync(CancellationToken.None);
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    internal static string NextNit() =>
        (810000000 + Interlocked.Increment(ref nitSequence)).ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Los servicios reales de Application e Infrastructure con la cola de la bandeja de salida. <see cref="MessageType"/>
/// es un tipo de mensaje único por cada host.
/// </summary>
public sealed class OutboxHost : IAsyncDisposable
{
    private readonly ServiceProvider services;

    private OutboxHost(IReadOnlyDictionary<string, string?> settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Postgres",
            ["Database:ConnectionString"] = OutboxDatabase.ConnectionString,
        };
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddInfrastructure(configuration, new PostgresDatabaseProvider());
        collection.AddApplication();
        collection.AddOutboxProcessing(configuration);
        collection.AddSingleton<IClock>(Clock);
        services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public FixedClock Clock { get; } = new(new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero));

    /// <summary>Tipo de mensaje de las pruebas; único por cada host.</summary>
    public string MessageType { get; } = "Test.Handled." + Guid.NewGuid().ToString("N");

    /// <summary>Tipo de mensaje sin handler registrado; único por cada host.</summary>
    public string UnhandledType { get; } = "Test.Unhandled." + Guid.NewGuid().ToString("N");

    public IServiceProvider Services => services;

    public static OutboxHost Create(IReadOnlyDictionary<string, string?>? settings = null) =>
        new(settings ?? new Dictionary<string, string?>());

    /// <summary>Abre un ámbito de servicios como proceso del sistema, que puede escribir para cualquier empresa.</summary>
    public AsyncServiceScope OpenScope()
    {
        var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(companyId: null, userId: null, ActorType.System);
        return scope;
    }

    /// <summary>Crea y guarda una empresa nueva, sin filas hijas.</summary>
    public async Task<Guid> CreateCompanyAsync()
    {
        var id = Guid.CreateVersion7();
        await using var scope = OpenScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
        db.Add(ModelGraph.NewCompany(id, OutboxDatabase.NextNit()));
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>Encola un mensaje con la bandeja de salida real y lo confirma. Por defecto, del tipo con handler.</summary>
    public async Task<Guid> EnqueueAsync(Guid? companyId, string? type = null, DateTimeOffset? notBefore = null)
    {
        await using var scope = OpenScope();
        var id = await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(
            type ?? MessageType, new { Reference = "R-1" }, companyId, notBefore, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().SaveChangesAsync();
        return id;
    }

    public async Task<OutboxMessage> FindAsync(Guid id)
    {
        await using var scope = OpenScope();
        return await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().Set<OutboxMessage>()
            .AsNoTracking()
            .SingleAsync(message => message.Id == id);
    }

    public async Task<List<Notification>> NotificationsAsync(Guid companyId)
    {
        await using var scope = OpenScope();
        return await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().Set<Notification>()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .AsNoTracking()
            .Where(notification => notification.CompanyId == companyId)
            .ToListAsync();
    }

    /// <summary>Notificaciones de cualquier empresa que nombran el mensaje.</summary>
    public async Task<int> CountNotificationsAboutAsync(Guid messageId)
    {
        await using var scope = OpenScope();
        var id = messageId.ToString();
        return await scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>().Set<Notification>()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .CountAsync(notification => notification.Body.Contains(id));
    }

    public ValueTask DisposeAsync() => services.DisposeAsync();
}
