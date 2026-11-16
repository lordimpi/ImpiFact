using System.Net;
using System.Text.Json;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Traducción de los errores de los casos de uso a HTTP (Arquitectura detallada, sección 9.4). El cuerpo es un
/// ProblemDetails (RFC 9457: formato estándar de errores en JSON) con los campos heredados del PHP
/// <c>success</c> y <c>message</c>, y el identificador de correlación de la solicitud.
/// </summary>
[TestFixture]
public sealed class ErrorMappingTests
{
    private IdentityApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi() => _factory = new IdentityApiFactory();

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    [TestCase(ErrorType.Validation, 422)]
    [TestCase(ErrorType.NotFound, 404)]
    [TestCase(ErrorType.Conflict, 409)]
    [TestCase(ErrorType.Unauthorized, 401)]
    [TestCase(ErrorType.Forbidden, 403)]
    [TestCase(ErrorType.DianRejected, 422)]
    [TestCase(ErrorType.Unavailable, 503)]
    [TestCase(ErrorType.Unexpected, 500)]
    public async Task Each_error_type_maps_to_its_status_with_problem_details_and_legacy_fields(ErrorType type, int status)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/test/errors/{type}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(status));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(root.GetProperty("status").GetInt32(), Is.EqualTo(status));
            Assert.That(root.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("message").GetString(), Is.Not.Empty);
            Assert.That(root.GetProperty("correlation_id").GetString(), Is.Not.Empty);
        });
    }

    [TestCase(ErrorType.NotFound)]
    [TestCase(ErrorType.Conflict)]
    [TestCase(ErrorType.Forbidden)]
    public async Task The_use_case_message_and_code_reach_the_client(ErrorType type)
    {
        var expected = TestEndpoints.ErrorFor(type);
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/test/errors/{type}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Multiple(() =>
        {
            Assert.That(body.RootElement.GetProperty("message").GetString(), Is.EqualTo(expected.Message));
            Assert.That(body.RootElement.GetProperty("detail").GetString(), Is.EqualTo(expected.Message));
            Assert.That(body.RootElement.GetProperty("code").GetString(), Is.EqualTo(expected.Code));
        });
    }

    [Test]
    public async Task Validation_errors_are_grouped_by_field()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/test/errors/{ErrorType.Validation}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");

        Assert.Multiple(() =>
        {
            Assert.That(errors.GetProperty("nit").EnumerateArray().Select(item => item.GetString()),
                Is.EqualTo(new[] { "El NIT es obligatorio.", "El NIT solo admite dígitos." }));
            Assert.That(errors.GetProperty("dv").EnumerateArray().Select(item => item.GetString()),
                Is.EqualTo(new[] { "El DV es obligatorio." }));
            Assert.That(body.RootElement.GetProperty("code").GetString(), Is.EqualTo("validation.failed"));
        });
    }

    [Test]
    public async Task A_dian_rejection_carries_the_rejected_rules()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/test/errors/{ErrorType.DianRejected}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.That(body.RootElement.GetProperty("errors").GetProperty("FAD06").EnumerateArray().Select(item => item.GetString()),
            Is.EqualTo(new[] { "El CUFE no coincide." }));
    }

    [Test]
    public async Task Unavailable_tells_the_client_when_to_retry()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/test/errors/{ErrorType.Unavailable}");

        Assert.That(response.Headers.RetryAfter?.Delta, Is.GreaterThan(TimeSpan.Zero));
    }

    [Test]
    public async Task An_unexpected_error_result_hides_its_internal_message()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/test/errors/{ErrorType.Unexpected}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(body, Does.Not.Contain(TestEndpoints.InternalDetail));
    }

    [Test]
    public async Task An_unhandled_exception_answers_500_without_internal_details()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/test/throw");
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
            Assert.That(text, Does.Not.Contain(TestEndpoints.InternalDetail));
            Assert.That(text, Does.Not.Contain(nameof(InvalidOperationException)));
            Assert.That(body.RootElement.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(body.RootElement.GetProperty("correlation_id").GetString(), Is.Not.Empty);
        });
    }

    [Test]
    public async Task A_success_result_answers_200_with_its_value()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/test/ok");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.EqualTo("\"hola\""));
        });
    }

    [Test]
    public async Task Missing_credentials_answer_401_with_the_same_problem_shape()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/test/whoami");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(body.RootElement.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(body.RootElement.GetProperty("message").GetString(), Is.Not.Empty);
        });
    }

    [Test]
    public async Task The_correlation_id_from_the_client_is_echoed_in_the_header_and_the_body()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/test/errors/{ErrorType.NotFound}");
        request.Headers.Add("X-Correlation-Id", "client-id-123");

        using var response = await client.SendAsync(request);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Multiple(() =>
        {
            Assert.That(response.Headers.GetValues("X-Correlation-Id"), Is.EqualTo(new[] { "client-id-123" }));
            Assert.That(body.RootElement.GetProperty("correlation_id").GetString(), Is.EqualTo("client-id-123"));
        });
    }

    [Test]
    public async Task An_unsafe_correlation_id_is_replaced_by_a_generated_one()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/ok");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "bad id with spaces <script>");

        using var response = await client.SendAsync(request);
        var echoed = response.Headers.GetValues("X-Correlation-Id").Single();

        Assert.That(echoed, Does.Match("^[A-Za-z0-9-]{8,64}$"));
    }
}
