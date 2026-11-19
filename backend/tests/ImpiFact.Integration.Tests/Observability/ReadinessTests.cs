using System.Net;
using System.Text.Json;
using ImpiFact.Application.Abstractions.Dian;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Observability;
using ImpiFact.Integration.Tests.Identity;
using ImpiFact.Integration.Tests.Protection;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace ImpiFact.Integration.Tests.Observability;

/// <summary>
/// <c>/health/ready</c> de la API (Arquitectura detallada, sección 12): base de datos, almacenamiento y proveedor de
/// secretos (si alguno falla, 503) y la DIAN como degradada según las últimas llamadas (200, sin llamarla). Es anónima y
/// solo dice el estado de cada prueba. <c>/health/live</c> no depende de nada.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class ReadinessTests
{
    private PostgreSqlContainer container = null!;

    [OneTimeSetUp]
    public async Task StartDatabaseAsync()
    {
        container = new PostgreSqlBuilder("postgres:18.1-alpine").Build();
        await container.StartAsync();
    }

    [OneTimeTearDown]
    public async Task StopDatabaseAsync() => await container.DisposeAsync();

    [Test]
    public async Task With_every_dependency_up_the_api_is_ready_and_names_each_check_anonymously()
    {
        using var factory = Factory(container.GetConnectionString());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(response.Headers.CacheControl?.NoCache, Is.True);
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(
                """{"status":"Healthy","checks":{"database":"Healthy","storage":"Healthy","secrets":"Healthy","dian":"Healthy"}}"""));
            Assert.That(response.Headers.Contains("X-Correlation-Id"), Is.True, "The pipeline still runs for health routes.");
        });
    }

    [Test]
    public async Task An_unreachable_database_makes_the_api_not_ready_with_no_detail_while_it_stays_live()
    {
        using var factory = Factory(UnreachableDatabase.ConnectionString);
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/health/ready");
        using var live = await client.GetAsync("/health/live");

        var body = await ready.Content.ReadAsStringAsync();
        Assert.Multiple(async () =>
        {
            Assert.That(ready.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(Checks(body), Is.EqualTo(new Dictionary<string, string>
            {
                ["database"] = "Unhealthy", ["storage"] = "Healthy", ["secrets"] = "Healthy", ["dian"] = "Healthy",
            }));
            Assert.That(JsonDocument.Parse(body).RootElement.GetProperty("status").GetString(), Is.EqualTo("Unhealthy"));
            Assert.That(body, Does.Not.Contain("127.0.0.1").And.Not.Contain("impifact_unreachable").And.Not.Contain("Exception"));
            Assert.That(live.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await live.Content.ReadAsStringAsync(), Is.EqualTo("Healthy"));
        });
    }

    [Test]
    public async Task A_failing_storage_or_an_unreadable_master_key_makes_the_api_not_ready_without_their_errors()
    {
        using var scratch = new ProtectionScratch();
        var masterKey = scratch.WriteMasterKey("readiness.key", new byte[32]);
        using var factory = Factory(
            container.GetConnectionString(),
            services => services.AddSingleton<IBlobStorage>(new FailingStorage()),
            ("Secrets:LocalAesGcm:MasterKeyFile", masterKey));
        using var client = factory.CreateClient();
        File.Delete(masterKey);

        using var response = await client.GetAsync("/health/ready");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(Checks(body), Is.EqualTo(new Dictionary<string, string>
            {
                ["database"] = "Healthy", ["storage"] = "Unhealthy", ["secrets"] = "Unhealthy", ["dian"] = "Healthy",
            }));
            Assert.That(body, Does.Not.Contain(FailingStorage.Detail).And.Not.Contain("readiness.key"));
        });
    }

    [Test]
    public async Task Recent_failed_dian_calls_report_the_dian_degraded_with_200_without_calling_it()
    {
        using var factory = Factory(container.GetConnectionString());
        using var client = factory.CreateClient();
        var monitor = factory.Services.GetRequiredService<DianCallMonitor>();
        for (var call = 0; call < DianCallMonitor.MinimumCalls; call++)
        {
            monitor.Record(new DianCall(DianCallOutcome.NotSent, "", null, null, null, TimeSpan.Zero, "Connection refused"));
        }

        using var response = await client.GetAsync("/health/ready");

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(
                """{"status":"Degraded","checks":{"database":"Healthy","storage":"Healthy","secrets":"Healthy","dian":"Degraded"}}"""));
            Assert.That(monitor.Recent().Calls, Is.EqualTo(DianCallMonitor.MinimumCalls), "The probe does not call the DIAN.");
        });
    }

    private static IdentityApiFactory Factory(
        string connectionString, Action<IServiceCollection>? configureServices = null, params (string Key, string Value)[] settings)
    {
        var values = settings.ToDictionary(setting => setting.Key, setting => (string?)setting.Value);
        values["Database:ConnectionString"] = connectionString;
        // La base de la prueba está vacía: basta con que acepte conexiones.
        values["Database:CheckPendingMigrationsAtStartup"] = "false";
        return new IdentityApiFactory("Production", settings: values, configureServices: configureServices);
    }

    private static Dictionary<string, string> Checks(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("checks").EnumerateObject()
            .ToDictionary(check => check.Name, check => check.Value.GetString()!);

    private sealed class FailingStorage : IBlobStorage
    {
        public const string Detail = "disk-detail-that-must-not-leak";

        public StorageProvider Provider => StorageProvider.Local;

        public Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken ct) => throw new IOException(Detail);

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => throw new IOException(Detail);

        public Task DeleteAsync(string key, CancellationToken ct) => throw new IOException(Detail);

        public Uri CreateSignedUrl(string key, TimeSpan lifetime) => throw new IOException(Detail);
    }
}
