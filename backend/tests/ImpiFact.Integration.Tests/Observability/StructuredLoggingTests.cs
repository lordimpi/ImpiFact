using System.Net;
using System.Text.Json;
using ImpiFact.Api.Endpoints;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Infrastructure.Observability;
using ImpiFact.Integration.Tests.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Integration.Tests.Observability;

/// <summary>
/// Registros de la API con Serilog (Arquitectura detallada, sección 12): una línea JSON por evento con el
/// identificador de correlación, la empresa y el usuario, sin secretos aunque la solicitud los traiga o el código
/// los pase a un registro. La salida de la consola se captura en memoria (<see cref="LogOutput"/>).
/// </summary>
[TestFixture]
public sealed class StructuredLoggingTests
{
    private const string Password = "hunter2-password-value";
    private const string RefreshToken = "refresh-token-value-0123456789";
    private const string Pin = "4321-pin-value";
    private const string Cookie = "cookie-session-value-0123456789";
    private const string QueryToken = "query-token-value-0123456789";

    [Test]
    public async Task In_production_each_log_line_is_json_with_the_correlation_id_company_and_user()
    {
        var output = new LogCapture();
        using var factory = Factory("Production", output);
        var userId = Guid.CreateVersion7();
        var companyId = Guid.CreateVersion7();
        var jwt = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(userId, ["owner"], companyId).Value;
        using var client = factory.CreateClient(jwt);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/observability/log");
        request.Headers.Add("X-Correlation-Id", "client-correlation-42");

        using var response = await client.SendAsync(request);

        var lines = JsonLines(output);
        var probe = lines.Single(line => line.GetProperty("@m").GetString() == "Observability probe from the endpoint.");
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.GetValues("X-Correlation-Id"), Is.EqualTo(new[] { "client-correlation-42" }));
            Assert.That(probe.GetProperty("CorrelationId").GetString(), Is.EqualTo("client-correlation-42"));
            Assert.That(probe.GetProperty("CompanyId").GetString(), Is.EqualTo(companyId.ToString()));
            Assert.That(probe.GetProperty("UserId").GetString(), Is.EqualTo(userId.ToString()));
            Assert.That(probe.GetProperty("SourceContext").GetString(), Is.EqualTo("ImpiFact.Observability.Probe"));
            Assert.That(probe.GetProperty("RequestPath").GetString(), Is.EqualTo("/test/observability/log"));
            Assert.That(probe.TryGetProperty("@t", out _), Is.True);
        });
    }

    [Test]
    public async Task Secrets_in_the_request_or_passed_to_a_log_never_reach_the_log_output()
    {
        var output = new LogCapture();
        using var factory = Factory("Production", output);
        var (apiKey, _) = factory.AddApiKey(Guid.CreateVersion7(), [ApiScopes.DocumentsRead]);
        var jwt = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(Guid.CreateVersion7(), ["owner"], Guid.CreateVersion7()).Value;
        string[] secrets = [apiKey, jwt, Password, RefreshToken, Pin, Cookie, QueryToken, Convert.ToHexString(Certificate)[..32]];

        foreach (var bearer in new[] { apiKey, jwt })
        {
            using var client = factory.CreateClient(bearer);
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"/test/observability/secrets?password={Password}&refresh_token={RefreshToken}&pin={Pin}&access_token={QueryToken}");
            request.Headers.Add("Cookie", $"impifact_session={Cookie}");
            using var response = await client.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        var text = output.ToString();
        Assert.Multiple(() =>
        {
            // Prueba de que se registró la solicitud (con su consulta) y lo que el endpoint pasó al registro.
            Assert.That(text, Does.Contain("/test/observability/secrets"));
            Assert.That(text, Does.Contain("Secret-bearing values were logged"));
            Assert.That(text, Does.Contain("password=***").And.Contain("refresh_token=***").And.Contain("pin=***"));
            Assert.That(text, Does.Contain($"[{Certificate.Length} bytes]").And.Contain("[64 bytes]"));
            foreach (var secret in secrets)
            {
                Assert.That(text, Does.Not.Contain(secret), $"A secret starting with '{secret[..6]}' was logged.");
            }
        });
    }

    [Test]
    public async Task In_development_the_console_is_readable_text_and_Logging_LogLevel_still_filters_by_category()
    {
        var output = new LogCapture();
        using var factory = Factory("Development", output, ("Logging:LogLevel:Microsoft.AspNetCore", "Warning"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/test/observability/log");

        var text = output.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(text, Does.Contain("INF] ImpiFact.Observability.Probe: Observability probe from the endpoint."));
            Assert.That(text.Split('\n', StringSplitOptions.RemoveEmptyEntries), Has.None.StartsWith("{"));
            Assert.That(text, Does.Not.Contain("Microsoft.AspNetCore.Hosting"), "Microsoft.AspNetCore is filtered at Warning.");
        });
    }

    [Test]
    public void An_unknown_log_format_stops_the_api_at_startup()
    {
        using var factory = Factory("Production", new LogCapture(), (ImpiFactLogging.FormatKey, "xml"));

        var exception = Assert.Catch(() => factory.CreateClient());

        Assert.That(exception?.ToString(), Does.Contain("Observability:LogFormat must be 'json' or 'text'"));
    }

    [TestCase("Authorization", true)]
    [TestCase("RefreshToken", true)]
    [TestCase("refresh_token", true)]
    [TestCase("NewPassword", true)]
    [TestCase("Pin", true)]
    [TestCase("pin_code", true)]
    [TestCase("ApiKey", true)]
    [TestCase("SigningKey", true)]
    [TestCase("ClientSecret", true)]
    [TestCase("Cookie", true)]
    [TestCase("impifact_refresh", true)]
    [TestCase("SessionId", true)]
    [TestCase("ShippingAddress", false)]
    [TestCase("Mapping", false)]
    [TestCase("DocumentId", false)]
    [TestCase("ZipKey", false)]
    public void Sensitive_names_are_recognised_by_word(string name, bool sensitive) =>
        Assert.That(SensitiveDataRedactor.IsSensitiveName(name), Is.EqualTo(sensitive));

    private static readonly byte[] Certificate = [.. Enumerable.Range(0, 2048).Select(index => (byte)(index * 7 % 251))];

    private static IdentityApiFactory Factory(string environment, LogCapture output, params (string Key, string Value)[] settings) =>
        new(
            environment,
            settings: settings.ToDictionary(setting => setting.Key, setting => (string?)setting.Value),
            configureServices: services =>
            {
                services.AddSingleton(new LogOutput(output));
                services.AddSingleton<IEndpointModule, ProbeEndpoints>();
            });

    private static List<JsonElement> JsonLines(LogCapture output) =>
        [.. output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonDocument.Parse(line).RootElement)];

    private sealed class ProbeEndpoints : IEndpointModule
    {
        public void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/test/observability/log", (ILoggerFactory loggers) =>
            {
                loggers.CreateLogger("ImpiFact.Observability.Probe").LogInformation("Observability probe from the endpoint.");
                return "ok";
            }).AllowAnonymous();

            endpoints.MapGet("/test/observability/secrets", (HttpContext context, ILoggerFactory loggers) =>
            {
                var logger = loggers.CreateLogger("ImpiFact.Observability.Probe");
                logger.LogInformation(
                    "Secret-bearing values were logged: {Password} {RefreshToken} {Pin} {Authorization} {Cookie} {Certificate} {SmallCertificate}",
                    Password, RefreshToken, Pin, context.Request.Headers.Authorization.ToString(), context.Request.Headers.Cookie.ToString(),
                    Certificate, Certificate[..64]);
                logger.LogInformation("The client sent {Header} and asked for {Query}.",
                    context.Request.Headers.Authorization.ToString(), context.Request.QueryString.Value);
                using (logger.BeginScope(new Dictionary<string, object> { ["ApiKey"] = context.Request.Headers.Authorization.ToString() }))
                {
                    logger.LogInformation("Inside a scope with a credential.");
                }

                return "ok";
            }).AllowAnonymous();
        }
    }
}
