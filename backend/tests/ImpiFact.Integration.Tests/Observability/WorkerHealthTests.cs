extern alias WorkerHost;

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ImpiFact.Infrastructure.Platform.Outbox;
using ImpiFact.Infrastructure.Postgres;
using ImpiFact.Integration.Tests.Protection;
using WorkerHost::ImpiFact.Worker.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ImpiFact.Integration.Tests.Observability;

/// <summary>
/// Salud del Worker por HTTP (Arquitectura detallada, sección 12): <c>/health/live</c> y <c>/health/ready</c> en
/// <c>WorkerHealth:Port</c>, para las sondas de los orquestadores. Con la base caída sigue vivo y no está listo. Sin
/// el puerto no abre ninguno. No necesita Docker: la base es inalcanzable.
/// </summary>
[TestFixture]
public sealed class WorkerHealthTests
{
    [Test]
    public async Task The_worker_answers_live_and_ready_on_its_health_port_without_details()
    {
        using var scratch = new ProtectionScratch();
        var port = FreePort();
        using var host = Worker(scratch, port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await host.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            using var live = await client.GetAsync("/health/live");
            using var ready = await client.GetAsync("/health/ready");
            using var other = await client.GetAsync("/api/v1/documents");

            var body = await ready.Content.ReadAsStringAsync();
            Assert.Multiple(async () =>
            {
                Assert.That(live.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(await live.Content.ReadAsStringAsync(), Is.EqualTo("Healthy"));
                Assert.That(ready.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
                Assert.That(ready.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
                Assert.That(JsonDocument.Parse(body).RootElement.GetProperty("checks").EnumerateObject()
                    .ToDictionary(check => check.Name, check => check.Value.GetString()), Is.EqualTo(new Dictionary<string, string?>
                    {
                        ["database"] = "Unhealthy", ["storage"] = "Healthy", ["secrets"] = "Healthy", ["dian"] = "Healthy",
                    }));
                Assert.That(body, Does.Not.Contain("127.0.0.1").And.Not.Contain("impifact_unreachable"));
                Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "Only the health routes are served.");
            });
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task Without_a_health_port_the_worker_opens_no_port()
    {
        using var scratch = new ProtectionScratch();
        var port = FreePort();
        using var host = Worker(scratch, port: null);
        await host.StartAsync();
        try
        {
            using var client = new HttpClient();

            Assert.That(
                async () => await client.GetAsync($"http://127.0.0.1:{port}/health/live"),
                Throws.InstanceOf<HttpRequestException>());
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public void A_health_port_out_of_range_stops_the_worker_at_startup()
    {
        using var scratch = new ProtectionScratch();
        using var host = Worker(scratch, "70000");

        var exception = Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.That(exception?.Message, Does.Contain(nameof(WorkerHealthOptions.Port)));
    }

    /// <summary>El Worker como lo arma <c>ImpiFact.Worker</c>, sin el procesador de la bandeja (no hay base).</summary>
    private static IHost Worker(ProtectionScratch scratch, string? port)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(scratch.LocalAesGcmSettings())
        {
            ["Database:Provider"] = "Postgres",
            ["Database:ConnectionString"] = UnreachableDatabase.ConnectionString,
            ["Dian:HabilitacionUrl"] = "https://vpfe-hab.dian.test/WcfDianCustomerServices.svc",
            ["Dian:ProduccionUrl"] = "https://vpfe.dian.test/WcfDianCustomerServices.svc",
            ["Dian:HabilitacionQrUrl"] = "https://catalogo-vpfe-hab.dian.test/document/searchqr?documentkey=",
            ["Dian:ProduccionQrUrl"] = "https://catalogo-vpfe.dian.test/document/searchqr?documentkey=",
            ["Dian:TimeoutSeconds"] = "60",
            ["Secrets:Provider"] = "LocalAesGcm",
            ["Storage:Provider"] = "Local",
            ["Storage:Local:Root"] = Path.Combine(scratch.Root, "storage"),
            ["Jwt:Issuer"] = "impifact",
            ["Jwt:Audience"] = "impifact-web",
            ["ApiKey:Prefix"] = "apk_",
            ["Panel:BaseUrl"] = "https://panel.impifact.test",
            ["Email:Host"] = "smtp.platform.test",
            ["Email:FromAddress"] = "no-reply@platform.test",
            ["ScheduledJobs:RunOnStartup"] = "false",
            ["WorkerHealth:Port"] = port,
        });
        builder.Services.AddWorkerServices(builder.Configuration, new PostgresDatabaseProvider());
        builder.Services.AddWorkerHealthEndpoints(builder.Configuration);
        // Solo el servidor de salud: el procesador de la bandeja y las tareas diarias necesitan la base.
        var background = builder.Services.Where(service => service.ServiceType == typeof(IHostedService)).ToList();
        foreach (var service in background.Where(service => service.ImplementationType != typeof(WorkerHealthServer)))
        {
            builder.Services.Remove(service);
        }

        return builder.Build();
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
