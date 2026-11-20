using System.Diagnostics;
using System.Text.Json;
using ImpiFact.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Integration.Tests.Observability;

/// <summary>
/// Lo que la barrera de datos sensibles cubre además de las propiedades de texto (Arquitectura detallada, sección 12):
/// el texto de las excepciones (con sus internas) en cada destino, los valores que no son texto pero se escriben como
/// texto (<see cref="Uri"/>) y las cookies de sesión y de renovación aunque se registren con un nombre neutro. Cada
/// prueba revisa que el secreto no salga y que el resto del contexto sí.
/// </summary>
[TestFixture]
public sealed class LogRedactionTests
{
    [Test]
    public void An_exception_message_with_a_credential_is_masked_in_the_json_console()
    {
        var output = new LogCapture();
        using (var services = Logging(output))
        {
            Logger(services).LogError(new Exception("Bearer abc123"), "x");
        }

        var line = JsonLines(output).Single();
        Assert.Multiple(() =>
        {
            Assert.That(output.ToString(), Does.Not.Contain("abc123"));
            Assert.That(line.GetProperty("@x").GetString(), Is.EqualTo("System.Exception: Bearer ***"));
            Assert.That(line.GetProperty("@m").GetString(), Is.EqualTo("x"));
            Assert.That(line.TryGetProperty("@tr", out _), Is.False, "Without an activity the copied event has no trace.");
        });
    }

    [Test]
    public void A_thrown_exception_keeps_its_type_message_and_stack_trace_without_the_secret_in_the_json_console()
    {
        var output = new LogCapture();
        var thrown = ThrowForTheStackTrace(() => new InvalidOperationException(
            "The DIAN call to https://vpfe.dian.test/ws?token=query-secret-123 failed with Bearer header-secret-456."));
        using (var services = Logging(output))
        {
            Logger(services).LogError(thrown, "Sending document {DocumentId} failed.", 42);
        }

        var line = JsonLines(output).Single();
        var exception = line.GetProperty("@x").GetString();
        Assert.Multiple(() =>
        {
            Assert.That(output.ToString(), Does.Not.Contain("query-secret-123").And.Not.Contain("header-secret-456"));
            Assert.That(exception, Does.StartWith(
                "System.InvalidOperationException: The DIAN call to https://vpfe.dian.test/ws?token=*** failed with Bearer ***"));
            Assert.That(exception, Does.Contain($"{nameof(LogRedactionTests)}.{nameof(ThrowForTheStackTrace)}"));
            Assert.That(line.GetProperty("@m").GetString(), Is.EqualTo("Sending document 42 failed."));
        });
    }

    [Test]
    public void An_exception_with_a_credential_is_masked_in_the_text_console()
    {
        var output = new LogCapture();
        var thrown = ThrowForTheStackTrace(() => new InvalidOperationException("Login with password=text-secret-789 failed."));
        using (var services = Logging(output, "text"))
        {
            Logger(services).LogError(thrown, "Login failed.");
        }

        var text = output.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Not.Contain("text-secret-789"));
            Assert.That(text, Does.Contain("ERR] ImpiFact.Redaction.Probe: Login failed."));
            Assert.That(text, Does.Contain("System.InvalidOperationException: Login with password=*** failed."));
            Assert.That(text, Does.Contain(nameof(ThrowForTheStackTrace)));
        });
    }

    [Test]
    public void Inner_and_aggregated_exceptions_are_masked_too()
    {
        var output = new LogCapture();
        var inner = ThrowForTheStackTrace(() => new HttpRequestException("Call to https://api.test/x?access_token=inner-secret-1 failed."));
        var aggregate = new AggregateException(
            new InvalidOperationException("First with Bearer aggregate-secret-2"),
            new InvalidOperationException("Second with apikey=aggregate-secret-3"));
        using (var services = Logging(output))
        {
            Logger(services).LogError(new InvalidOperationException("Outer failure.", inner), "Inner.");
            Logger(services).LogError(aggregate, "Aggregate.");
        }

        var lines = JsonLines(output);
        var withInner = lines.Single(line => line.GetProperty("@m").GetString() == "Inner.").GetProperty("@x").GetString();
        var aggregated = lines.Single(line => line.GetProperty("@m").GetString() == "Aggregate.").GetProperty("@x").GetString();
        Assert.Multiple(() =>
        {
            foreach (var secret in new[] { "inner-secret-1", "aggregate-secret-2", "aggregate-secret-3" })
            {
                Assert.That(output.ToString(), Does.Not.Contain(secret));
            }

            Assert.That(withInner, Does.StartWith("System.InvalidOperationException: Outer failure."));
            Assert.That(withInner, Does.Contain(
                "---> System.Net.Http.HttpRequestException: Call to https://api.test/x?access_token=*** failed."));
            Assert.That(withInner, Does.Contain(nameof(ThrowForTheStackTrace)));
            Assert.That(aggregated, Does.StartWith("System.AggregateException:"));
            Assert.That(aggregated, Does.Contain("System.InvalidOperationException: First with Bearer ***"));
            Assert.That(aggregated, Does.Contain("System.InvalidOperationException: Second with apikey=***"));
        });
    }

    // Las excepciones de los SDK de las nubes pasan por la misma limpieza; las firmas de las URL firmadas (sig de una SAS
    // de Azure, X-Amz-Signature y X-Goog-Signature) valen lo que una credencial mientras la URL no vence.
    [Test]
    public void Cloud_sdk_exceptions_and_signed_url_signatures_are_masked()
    {
        var output = new LogCapture();
        var azure = ThrowForTheStackTrace(() => new Azure.RequestFailedException(
            403, "Download of https://impifact.blob.core.windows.net/docs/a.pdf?sv=2025-01-05&sp=r&sig=azure-secret-1 was denied."));
        var aws = ThrowForTheStackTrace(() => new Amazon.SecretsManager.Model.InvalidRequestException(
            "Call to https://bucket.s3.amazonaws.com/a.pdf?X-Amz-Expires=300&X-Amz-Signature=aws-secret-2 failed."));
        var gcp = ThrowForTheStackTrace(() => new Grpc.Core.RpcException(new Grpc.Core.Status(
            Grpc.Core.StatusCode.PermissionDenied, "Denied https://storage.googleapis.com/b/a.pdf?X-Goog-Signature=gcp-secret-3 with Bearer gcp-secret-4")));
        using (var services = Logging(output))
        {
            Logger(services).LogError(azure, "Azure.");
            Logger(services).LogError(aws, "Aws.");
            Logger(services).LogError(gcp, "Gcp.");
        }

        var lines = JsonLines(output);
        string Exception(string message) => lines.Single(line => line.GetProperty("@m").GetString() == message).GetProperty("@x").GetString()!;
        Assert.Multiple(() =>
        {
            foreach (var secret in new[] { "azure-secret-1", "aws-secret-2", "gcp-secret-3", "gcp-secret-4" })
            {
                Assert.That(output.ToString(), Does.Not.Contain(secret));
            }

            Assert.That(Exception("Azure."), Does.StartWith("Azure.RequestFailedException:").And.Contain("sp=r&sig=***"));
            Assert.That(Exception("Aws."), Does.StartWith("Amazon.SecretsManager.Model.InvalidRequestException:").And.Contain("X-Amz-Expires=300&X-Amz-Signature=***"));
            Assert.That(Exception("Gcp."), Does.StartWith("Grpc.Core.RpcException:").And.Contain("X-Goog-Signature=***").And.Contain("Bearer ***"));
        });
    }

    [Test]
    public void An_exception_without_secrets_is_written_as_is()
    {
        var output = new LogCapture();
        var thrown = ThrowForTheStackTrace(() => new InvalidOperationException("Nothing sensitive here."));
        using (var services = Logging(output))
        {
            Logger(services).LogError(thrown, "Plain.");
        }

        Assert.That(JsonLines(output).Single().GetProperty("@x").GetString(), Is.EqualTo(thrown.ToString()));
    }

    [Test]
    public void Masking_an_exception_keeps_the_trace_and_span_of_the_event()
    {
        var output = new LogCapture();
        using var activity = new Activity("redaction-probe").Start();
        using (var services = Logging(output))
        {
            Logger(services).LogError(new Exception("Bearer trace-secret-1"), "Traced.");
        }

        var line = JsonLines(output).Single();
        Assert.Multiple(() =>
        {
            Assert.That(output.ToString(), Does.Not.Contain("trace-secret-1"));
            Assert.That(line.GetProperty("@tr").GetString(), Is.EqualTo(activity.TraceId.ToHexString()));
            Assert.That(line.GetProperty("@sp").GetString(), Is.EqualTo(activity.SpanId.ToHexString()));
        });
    }

    [Test]
    public async Task An_exception_with_a_credential_is_masked_in_the_logs_sent_by_otlp()
    {
        await using var receiver = await OtlpReceiver.StartAsync();
        var thrown = ThrowForTheStackTrace(() => new InvalidOperationException("Upload with Bearer otlp-secret-1 failed."));
        using (var services = Logging(
            new LogCapture(),
            "json",
            (ImpiFactLogging.OtlpEndpointKey, receiver.Endpoint),
            (ImpiFactLogging.OtlpProtocolKey, "http/protobuf")))
        {
            Logger(services).LogError(thrown, "Upload failed.");
        }

        // Al cerrar el registro, Serilog manda lo que le quedaba.
        var body = receiver.BodyOf("/v1/logs");
        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain("Upload failed."));
            Assert.That(body, Does.Not.Contain("otlp-secret-1"));
            Assert.That(body, Does.Contain("Upload with Bearer *** failed."));
            Assert.That(body, Does.Contain("System.InvalidOperationException"));
            Assert.That(body, Does.Contain(nameof(ThrowForTheStackTrace)));
        });
    }

    [Test]
    public void A_uri_with_a_sensitive_query_parameter_is_masked_and_other_scalars_keep_their_type()
    {
        var output = new LogCapture();
        var id = Guid.CreateVersion7();
        var when = new DateTimeOffset(2026, 11, 19, 8, 30, 0, TimeSpan.Zero);
        using (var services = Logging(output))
        {
            Logger(services).LogInformation(
                "Calling {Endpoint} for {Id} at {When}: {Count} items, {Total} total, sent {Sent}.",
                new Uri("https://vpfe.dian.test/document/searchqr?documentkey=123&token=uri-secret-1"), id, when, 42, 12.5m, true);
        }

        var line = JsonLines(output).Single();
        Assert.Multiple(() =>
        {
            Assert.That(output.ToString(), Does.Not.Contain("uri-secret-1"));
            Assert.That(line.GetProperty("Endpoint").GetString(),
                Is.EqualTo("https://vpfe.dian.test/document/searchqr?documentkey=123&token=***"));
            Assert.That(line.GetProperty("Id").GetString(), Is.EqualTo(id.ToString()));
            Assert.That(line.GetProperty("When").GetDateTimeOffset(), Is.EqualTo(when));
            Assert.That(line.GetProperty("Count").ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(line.GetProperty("Count").GetInt32(), Is.EqualTo(42));
            Assert.That(line.GetProperty("Total").GetDecimal(), Is.EqualTo(12.5m));
            Assert.That(line.GetProperty("Sent").GetBoolean(), Is.True);
        });
    }

    [Test]
    public void A_uri_without_secrets_is_written_as_is()
    {
        var output = new LogCapture();
        using (var services = Logging(output))
        {
            Logger(services).LogInformation("Calling {Endpoint}.", new Uri("https://vpfe.dian.test/document/searchqr?documentkey=123"));
        }

        Assert.That(JsonLines(output).Single().GetProperty("Endpoint").GetString(),
            Is.EqualTo("https://vpfe.dian.test/document/searchqr?documentkey=123"));
    }

    [TestCase("Cookie: impifact_refresh=abc", "Cookie: impifact_refresh=***")]
    [TestCase("impifact_session=abc; theme=dark", "impifact_session=***; theme=dark")]
    [TestCase(
        "Set-Cookie: impifact_refresh=abc-def; Path=/api/v1/auth; Expires=Thu, 03 Dec 2026 08:00:00 GMT; HttpOnly; Secure; SameSite=Strict",
        "Set-Cookie: impifact_refresh=***; Path=/api/v1/auth; Expires=Thu, 03 Dec 2026 08:00:00 GMT; HttpOnly; Secure; SameSite=Strict")]
    [TestCase("Cookie: impifact_refresh=\"quoted value\"; lang=es", "Cookie: impifact_refresh=***; lang=es")]
    [TestCase("sessionid=abc&page=2", "sessionid=***&page=2")]
    public void Refresh_and_session_cookies_are_masked_and_their_attributes_kept(string text, string expected) =>
        Assert.That(SensitiveDataRedactor.RedactText(text), Is.EqualTo(expected));

    [Test]
    public void A_refresh_cookie_logged_under_a_neutral_name_is_masked()
    {
        var output = new LogCapture();
        using (var services = Logging(output))
        {
            Logger(services).LogInformation("The client sent {Header}.", "impifact_refresh=cookie-secret-1; impifact_csrf_hint=1");
        }

        Assert.Multiple(() =>
        {
            Assert.That(output.ToString(), Does.Not.Contain("cookie-secret-1"));
            Assert.That(JsonLines(output).Single().GetProperty("Header").GetString(),
                Is.EqualTo("impifact_refresh=***; impifact_csrf_hint=1"));
        });
    }

    /// <summary>Lanza y atrapa la excepción, para que tenga una traza con este método.</summary>
    private static Exception ThrowForTheStackTrace(Func<Exception> create)
    {
        try
        {
            throw create();
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    /// <summary>Los registros como los arman la API y el Worker, con la consola en <paramref name="output"/>.</summary>
    private static ServiceProvider Logging(LogCapture output, string format = "json", params (string Key, string Value)[] settings)
    {
        var values = new Dictionary<string, string?> { [ImpiFactLogging.FormatKey] = format };
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton(new LogOutput(output));
        services.AddLogging(logging => logging.AddImpiFactSerilog(
            configuration, new HostingEnvironment { EnvironmentName = "Production" }, "impifact-test"));
        return services.BuildServiceProvider();
    }

    private static ILogger Logger(IServiceProvider services) =>
        services.GetRequiredService<ILoggerFactory>().CreateLogger("ImpiFact.Redaction.Probe");

    private static List<JsonElement> JsonLines(LogCapture output) =>
        [.. output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonDocument.Parse(line).RootElement)];
}
