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
/// <c>WorkerHealth:Host</c> y <c>WorkerHealth:Port</c>, para las sondas de los orquestadores. Con la base caída sigue
/// vivo y no está listo. Sin el puerto no abre ninguno; sin la dirección escucha solo en 127.0.0.1 (los contenedores
/// fijan <c>0.0.0.0</c>). No necesita Docker: la base es inalcanzable.
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
    public async Task Without_a_host_the_health_port_listens_only_on_loopback()
    {
        var lan = LanAddress();
        Assume.That(lan, Is.Not.Null, "The machine has no non-loopback IPv4 address to probe.");
        using var scratch = new ProtectionScratch();
        var port = FreePort();
        using var host = Worker(scratch, port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await host.StartAsync();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            using var loopback = await client.GetAsync($"http://127.0.0.1:{port}/health/live");

            Assert.Multiple(() =>
            {
                Assert.That(host.Services.GetRequiredService<IOptions<WorkerHealthOptions>>().Value.Host, Is.EqualTo("127.0.0.1"));
                Assert.That(loopback.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(
                    async () => await client.GetAsync($"http://{lan}:{port}/health/live"),
                    Throws.InstanceOf<HttpRequestException>(), "The health port must not be reachable from the network by default.");
            });
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task With_host_0_0_0_0_the_health_port_answers_on_every_interface()
    {
        var lan = LanAddress();
        Assume.That(lan, Is.Not.Null, "The machine has no non-loopback IPv4 address to probe.");
        using var scratch = new ProtectionScratch();
        var port = FreePort();
        using var host = Worker(scratch, port.ToString(System.Globalization.CultureInfo.InvariantCulture), "0.0.0.0");
        await host.StartAsync();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            using var fromTheNetwork = await client.GetAsync($"http://{lan}:{port}/health/live");

            Assert.That(fromTheNetwork.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public void A_health_host_that_is_not_an_ip_address_stops_the_worker_at_startup()
    {
        using var scratch = new ProtectionScratch();
        using var host = Worker(scratch, "8091", "health.example");

        var exception = Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.That(exception?.Message, Does.Contain(nameof(WorkerHealthOptions.Host)).And.Contain("health.example"));
    }

    [Test]
    public void The_worker_settings_open_the_health_port_8091_on_loopback_only()
    {
        // 8081 es del ApiDian de referencia de backend/tools/golden-capture.
        var settings = new ConfigurationBuilder().AddJsonFile(WorkerSettingsFile(), optional: false).Build();

        Assert.Multiple(() =>
        {
            Assert.That(settings["WorkerHealth:Port"], Is.EqualTo("8091"));
            Assert.That(settings["WorkerHealth:Host"], Is.EqualTo("127.0.0.1"));
        });
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
    private static IHost Worker(ProtectionScratch scratch, string? port, string? healthHost = null)
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
        if (healthHost is not null)
        {
            builder.Configuration["WorkerHealth:Host"] = healthHost;
        }

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

    /// <summary>Una dirección IPv4 de esta máquina que no es la de loopback, o nulo si no tiene.</summary>
    private static IPAddress? LanAddress() =>
        System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address));

    private static string WorkerSettingsFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ImpiFact.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("The backend folder was not found."),
            "src", "ImpiFact.Worker", "appsettings.json");
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
