using System.Collections.Concurrent;
using System.Net;
using System.Text;
using ImpiFact.Integration.Tests.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace ImpiFact.Integration.Tests.Observability;

/// <summary>
/// Trazas, métricas y registros por OTLP (Arquitectura detallada, sección 12): solo si está la variable estándar
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>. Un receptor OTLP falso (HTTP con protobuf) en la propia prueba recibe lo que
/// manda la API; sin la variable no se registra nada de OpenTelemetry y no sale nada.
/// </summary>
[TestFixture]
public sealed class OtlpExportTests
{
    [Test]
    public async Task With_an_otlp_endpoint_the_api_exports_traces_metrics_and_logs_named_impifact_api()
    {
        await using var receiver = await OtlpReceiver.StartAsync();
        var factory = new IdentityApiFactory("Production", settings: new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = receiver.Endpoint,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
        });
        using (factory)
        {
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/test/ok");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(factory.Services.GetRequiredService<TracerProvider>().ForceFlush(10_000), Is.True);
            Assert.That(factory.Services.GetRequiredService<MeterProvider>().ForceFlush(10_000), Is.True);
        }

        // Al cerrar la API, Serilog manda los registros que le quedaban.
        Assert.Multiple(() =>
        {
            Assert.That(receiver.BodyOf("/v1/traces"), Does.Contain("impifact-api").And.Contain("/test/ok"));
            Assert.That(receiver.BodyOf("/v1/metrics"), Does.Contain("impifact-api").And.Contain("http.server.request.duration"));
            Assert.That(receiver.BodyOf("/v1/logs"), Does.Contain("impifact-api"));
        });
    }

    [Test]
    public async Task Without_an_otlp_endpoint_nothing_of_opentelemetry_is_registered()
    {
        using var factory = new IdentityApiFactory("Production");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/test/ok");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(factory.Services.GetService<TracerProvider>(), Is.Null);
            Assert.That(factory.Services.GetService<MeterProvider>(), Is.Null);
        });
    }

    /// <summary>Recibe en un puerto local lo que llegue por POST y guarda el cuerpo por ruta.</summary>
    private sealed class OtlpReceiver : IAsyncDisposable
    {
        private readonly WebApplication app;
        private readonly ConcurrentDictionary<string, ConcurrentQueue<byte[]>> bodies = new();

        private OtlpReceiver(WebApplication app) => this.app = app;

        public string Endpoint => app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        public static async Task<OtlpReceiver> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            var receiver = new OtlpReceiver(app);
            app.MapPost("/{**path}", async (HttpContext context) =>
            {
                using var buffer = new MemoryStream();
                await context.Request.Body.CopyToAsync(buffer);
                receiver.bodies.GetOrAdd(context.Request.Path.Value!, _ => new()).Enqueue(buffer.ToArray());
                return Results.Ok();
            });
            await app.StartAsync();
            return receiver;
        }

        /// <summary>Los cuerpos recibidos en la ruta, como texto (los textos del protobuf viajan en UTF-8).</summary>
        public string BodyOf(string path) =>
            bodies.TryGetValue(path, out var received) ? string.Concat(received.Select(body => Encoding.UTF8.GetString(body))) : "";

        public async ValueTask DisposeAsync() => await app.DisposeAsync();
    }
}
