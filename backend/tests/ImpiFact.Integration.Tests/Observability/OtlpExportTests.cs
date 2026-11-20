using System.Net;
using ImpiFact.Integration.Tests.Identity;
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
}
