using System.Net.Http.Headers;
using System.Security.Cryptography;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Companies;
using ImpiFact.Domain.Identity;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Infrastructure.Tenancy;
using ImpiFact.Integration.Tests.Identity;
using ImpiFact.Integration.Tests.Persistence;
using ImpiFact.Integration.Tests.Protection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace ImpiFact.Integration.Tests.Companies;

/// <summary>
/// Levanta un PostgreSQL desechable en Docker y la API real encima, una sola vez para las pruebas de este espacio de
/// nombres (un <c>SetUpFixture</c> de NUnit solo cubre su propio espacio de nombres). El esquema sale del modelo con
/// las migraciones reales y el sincronizador de catálogos. Las API keys se leen de la tabla <c>api_keys</c> con el adaptador
/// real, así la llave que devuelve el alta de una empresa sirve en las solicitudes siguientes.
/// </summary>
[SetUpFixture]
[Category("Integration")]
public sealed class CompaniesHost
{
    /// <summary>Fecha fija de las pruebas: 15-ene-2026 a las 10:00 UTC (05:00 en Colombia).</summary>
    public static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private static PostgreSqlContainer? container;
    private static CompanyApiFactory? factory;

    public static CompanyApiFactory Api => factory ?? throw new InvalidOperationException("The company API host is not running.");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        container = new PostgreSqlBuilder("postgres:18.1-alpine").Build();
        await container.StartAsync();
        factory = new CompanyApiFactory(container.GetConnectionString());

        await using var scope = factory.OpenScope(companyId: null, userId: null, ActorType.System);
        // El esquema sale de las migraciones reales y los catálogos del sincronizador, como en un despliegue.
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

    /// <summary>Un NIT nuevo al azar con su dígito de verificación, para que las pruebas no choquen entre sí.</summary>
    public static (string Nit, string Dv) NewNit()
    {
        var nit = "9" + RandomNumberGenerator.GetInt32(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return (nit, Nit.CheckDigit(nit).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}

/// <summary>La API con la base de las pruebas, una llave maestra temporal y un reloj fijo.</summary>
public sealed class CompanyApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    private readonly ProtectionScratch _protection = new();
    private readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public string CertificateDirectory => _protection.CertificateDirectory;

    public FixedClock Clock { get; } = new(CompaniesHost.Now);

    /// <summary>Todo lo que la API escribe en los registros, para revisar que no filtre secretos.</summary>
    internal CapturingLoggerProvider Logs { get; } = new();

    /// <summary>Abre un ámbito con el contexto de base de datos y el tenant indicados.</summary>
    public HostScope OpenScope(Guid? companyId, Guid? userId, ActorType actor)
    {
        var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(companyId, userId, actor);
        return new HostScope(scope);
    }

    /// <summary>Crea una API key en <c>api_keys</c> y devuelve un cliente HTTP que la usa.</summary>
    public async Task<HttpClient> ClientWithNewKeyAsync(Guid? companyId, params string[] scopes)
    {
        var generated = Services.GetRequiredService<IApiKeyGenerator>().Generate();
        await using (var scope = OpenScope(null, null, ActorType.System))
        {
            scope.Db.Set<ApiKey>().Add(new ApiKey
            {
                CompanyId = companyId,
                Name = "test",
                KeyPrefix = generated.DisplayPrefix,
                KeyHash = generated.Hash,
                Scopes = string.Join(',', scopes),
            });
            await scope.Db.SaveChangesAsync();
        }

        return ClientWith(generated.PlainText);
    }

    public HttpClient ClientWith(string bearer)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:ConnectionString", connectionString);
        builder.UseSetting("Secrets:LocalAesGcm:MasterKeyFile", _protection.MasterKeyFile);
        builder.UseSetting("Secrets:LocalAesGcm:CertificateDirectory", _protection.CertificateDirectory);
        builder.UseSetting("Storage:Local:Root", Path.Combine(_protection.Root, "storage"));
        builder.UseSetting("Jwt:SigningKey", _signingKey.ExportPkcs8PrivateKeyPem());
        builder.UseSetting("RateLimit:PermitPerMinutePerKey", "10000");
        builder.UseSetting("RateLimit:PermitPerMinutePerIp", "10000");
        builder.UseSetting("Logging:LogLevel:Default", "Trace");
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IClock>(Clock);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _signingKey.Dispose();
            _protection.Dispose();
        }
    }
}

/// <summary>Un ámbito de la API: su contexto de base de datos y sus servicios.</summary>
public sealed class HostScope(AsyncServiceScope scope) : IAsyncDisposable
{
    public ImpiFactDbContext Db { get; } = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();

    public IServiceProvider Services => scope.ServiceProvider;

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}
