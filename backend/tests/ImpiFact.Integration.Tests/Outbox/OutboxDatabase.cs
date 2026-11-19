using System.Collections.Concurrent;
using System.Globalization;
using ImpiFact.Application;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Infrastructure.Platform.Outbox;
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
/// Los servicios reales de Application e Infrastructure con el procesador de la bandeja de salida y un handler de
/// prueba (<see cref="ScriptedHandler"/>) para <see cref="MessageType"/>. <see cref="UnhandledType"/> no tiene handler.
/// </summary>
public sealed class OutboxHost : IAsyncDisposable
{
    private readonly ServiceProvider services;

    private OutboxHost(IReadOnlyDictionary<string, string?> settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["Database:Provider"] = TestDatabase.ProviderName,
            ["Database:ConnectionString"] = OutboxDatabase.ConnectionString,
        };
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddInfrastructure(configuration, TestDatabase.Providers);
        collection.AddApplication();
        collection.AddOutboxProcessing(configuration);
        collection.AddSingleton<IClock>(Clock);
        collection.AddSingleton(Script);
        collection.AddScoped<IOutboxMessageHandler>(provider =>
            new ScriptedHandler(MessageType, provider.GetRequiredService<ITenantContext>(), provider.GetRequiredService<HandlerScript>()));
        services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public FixedClock Clock { get; } = new(new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero));

    /// <summary>Tipo de mensaje que procesa el handler de prueba; único por cada host.</summary>
    public string MessageType { get; } = "Test.Handled." + Guid.NewGuid().ToString("N");

    /// <summary>Tipo de mensaje sin handler registrado; único por cada host.</summary>
    public string UnhandledType { get; } = "Test.Unhandled." + Guid.NewGuid().ToString("N");

    public HandlerScript Script { get; } = new();

    public IServiceProvider Services => services;

    internal OutboxProcessor Processor => services.GetRequiredService<OutboxProcessor>();

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

/// <summary>Qué hace el handler de prueba con cada mensaje y qué vio al procesarlo.</summary>
public sealed class HandlerScript
{
    private readonly ConcurrentDictionary<Guid, Func<Exception?>> failures = new();

    /// <summary>Tenant que vio el handler en cada intento, por id de mensaje.</summary>
    public ConcurrentDictionary<Guid, (Guid? CompanyId, ActorType Actor, int Attempt)> Seen { get; } = new();

    /// <summary>Hace que el mensaje falle en cada intento con la excepción que crea <paramref name="failure"/>.</summary>
    public void FailWith(Guid messageId, Func<Exception> failure) => failures[messageId] = failure;

    internal Exception? FailureFor(Guid messageId) => failures.TryGetValue(messageId, out var failure) ? failure() : null;

    /// <summary>Mensajes cuyo handler no termina hasta que lo cancelan.</summary>
    public ConcurrentDictionary<Guid, bool> Hanging { get; } = new();

    /// <summary>Se ejecuta cuando el handler termina bien; sirve para simular que el Worker se apaga justo después.</summary>
    public Action? AfterSuccess { get; set; }

    /// <summary>Se ejecuta cuando el Worker se rinde con un mensaje (<see cref="IOutboxMessageHandler.OnFailedAsync"/>).</summary>
    public Func<Guid, Task>? OnFailed { get; set; }
}

/// <summary>Handler de prueba: anota el tenant que ve y falla cuando el guion lo pide.</summary>
public sealed class ScriptedHandler(string messageType, ITenantContext tenant, HandlerScript script) : IOutboxMessageHandler
{
    public string MessageType => messageType;

    public async Task HandleAsync(OutboxMessageContext message, CancellationToken ct)
    {
        script.Seen[message.MessageId] = (tenant.CompanyId, tenant.Actor, message.Attempt);
        if (script.Hanging.ContainsKey(message.MessageId))
        {
            await Task.Delay(Timeout.Infinite, ct);
        }

        if (script.FailureFor(message.MessageId) is { } failure)
        {
            throw failure;
        }

        script.AfterSuccess?.Invoke();
    }

    public Task OnFailedAsync(OutboxMessageContext message, CancellationToken ct) =>
        script.OnFailed?.Invoke(message.MessageId) ?? Task.CompletedTask;
}
