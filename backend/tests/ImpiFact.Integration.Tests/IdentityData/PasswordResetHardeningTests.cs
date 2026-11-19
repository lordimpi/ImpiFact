using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Features.Identity.PasswordReset;
using ImpiFact.Integration.Tests.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ImpiFact.Integration.Tests.IdentityData;

/// <summary>
/// Casos de seguridad de la recuperación de contraseña, contra PostgreSQL: la respuesta de la solicitud es idéntica
/// exista o no la cuenta, también con solicitudes simultáneas; confirmar a la vez con el mismo token deja ganar a una
/// sola; el token de un usuario inactivo no se gasta; y la bandeja de salida guarda solo el id y el token cifrado.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class PasswordResetHardeningTests
{
    private const string Password = "Contraseña-Segura-1";
    private const string RequestPath = "/api/v1/auth/password-reset/request";
    private const string ConfirmPath = "/api/v1/auth/password-reset/confirm";
    private static readonly string[] VolatileHeaders = ["Date", "X-Correlation-Id", "traceparent", "Request-Id"];
    private IdentityDataApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi() => _factory = new IdentityDataApiFactory();

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    [Test]
    public async Task A_request_answers_the_same_status_headers_and_body_for_active_unknown_and_inactive_emails()
    {
        var (_, active) = await AddUserAsync();
        var (inactiveId, inactive) = await AddUserAsync();
        await ExecuteAsync($"update users set is_active = {TestDatabase.Bool(false)} where id = @inactiveId", new { inactiveId });

        var shapes = new List<string>();
        foreach (var email in new[] { active, "nadie-" + active, inactive, "  " + active.ToUpperInvariant() + "  " })
        {
            using var response = await _factory.CreateClient().PostAsJsonAsync(RequestPath, new { email });
            var headers = response.Headers.Concat(response.Content.Headers)
                .Where(h => !VolatileHeaders.Contains(h.Key, StringComparer.OrdinalIgnoreCase))
                .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
                .Select(h => h.Key + "=" + string.Join(",", h.Value));
            shapes.Add($"{(int)response.StatusCode}|{string.Join(";", headers)}|{await response.Content.ReadAsStringAsync()}");
        }

        Assert.That(shapes.Distinct().Count(), Is.EqualTo(1), string.Join("\n", shapes));
    }

    [Test]
    public async Task Simultaneous_requests_for_an_existing_email_answer_like_an_unknown_email()
    {
        var (_, email) = await AddUserAsync();
        var unknown = "nadie-" + email;

        // La primera solicitud deja un token sin usar: las simultáneas que siguen tienen que dejarlo sin efecto a la vez.
        using (await _factory.CreateClient().PostAsJsonAsync(RequestPath, new { email }))
        {
        }

        using (await _factory.CreateClient().PostAsJsonAsync(RequestPath, new { email = unknown }))
        {
        }

        async Task<int[]> BurstAsync(string target)
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, 4)
                .Select(_ => _factory.CreateClient().PostAsJsonAsync(RequestPath, new { email = target })));
            return [.. responses.Select(r => (int)r.StatusCode)];
        }

        var forExisting = await BurstAsync(email);
        var forUnknown = await BurstAsync(unknown);

        Assert.Multiple(() =>
        {
            Assert.That(forUnknown, Is.All.EqualTo(202));
            Assert.That(forExisting, Is.All.EqualTo(202), "A 409 only for a registered email reveals that the account exists.");
        });
    }

    [Test]
    public async Task A_new_request_revokes_the_unused_token_and_moves_its_row_version_and_updated_at()
    {
        var (userId, email) = await AddUserAsync();
        await RequestTokenAsync(userId, email);
        await RequestTokenAsync(userId, email);

        var rows = (await QueryAsync<(DateTime? RevokedAt, long RowVersion, DateTime CreatedAt, DateTime UpdatedAt)>(
            $"select {TestDatabase.Utc("revoked_at")}, row_version, {TestDatabase.Utc("created_at")}, {TestDatabase.Utc("updated_at")} from password_reset_tokens where user_id = @userId " +
            "order by created_at, id", new { userId })).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(2));
            Assert.That(rows[0].RevokedAt, Is.Not.Null, "The older token is revoked.");
            Assert.That(rows[0].RowVersion, Is.EqualTo(2), "Revoking bumps row_version so a confirm that read the token earlier conflicts.");
            Assert.That(rows[0].UpdatedAt, Is.GreaterThan(rows[0].CreatedAt));
            Assert.That(rows[1].RevokedAt, Is.Null, "The newest token stays usable.");
            Assert.That(rows[1].RowVersion, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Concurrent_confirms_with_one_token_let_exactly_one_win_without_a_server_error()
    {
        var (userId, email) = await AddUserAsync();
        var token = await RequestTokenAsync(userId, email);
        var passwords = Enumerable.Range(0, 8).Select(i => $"Clave-Concurrente-{i:D2}").ToArray();

        var responses = await Task.WhenAll(passwords.Select(p =>
            _factory.CreateClient().PostAsJsonAsync(ConfirmPath, new { token, new_password = p })));
        var statuses = responses.Select(r => (int)r.StatusCode).ToArray();

        var logins = new List<int>();
        foreach (var p in passwords)
        {
            using var login = await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = p });
            logins.Add((int)login.StatusCode);
        }

        Assert.Multiple(() =>
        {
            Assert.That(statuses.Count(s => s == 204), Is.EqualTo(1), "Exactly one confirm wins.");
            Assert.That(statuses, Has.None.GreaterThanOrEqualTo(500), "Losers never get a server error.");
            Assert.That(logins.Count(s => s == 200), Is.EqualTo(1), "Exactly one new password works.");
            Assert.That(Array.IndexOf(statuses, 204), Is.EqualTo(logins.IndexOf(200)), "The winner's password is the one stored.");
        });
    }

    [Test]
    public async Task A_token_of_a_user_made_inactive_answers_the_generic_422_and_is_not_spent()
    {
        var (userId, email) = await AddUserAsync();
        var token = await RequestTokenAsync(userId, email);
        await ExecuteAsync($"update users set is_active = {TestDatabase.Bool(false)} where id = @userId", new { userId });

        using var response = await _factory.CreateClient().PostAsJsonAsync(ConfirmPath, new { token, new_password = "Otra-Clave-Segura-2" });
        var body = await response.Content.ReadAsStringAsync();
        var usedAt = await QuerySingleAsync<DateTime?>($"select {TestDatabase.Utc("used_at")} from password_reset_tokens where user_id = @userId", new { userId });

        Assert.Multiple(() =>
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(422));
            Assert.That(JsonDocument.Parse(body).RootElement.GetProperty("code").GetString(), Is.EqualTo("identity.invalid_reset_token"));
            Assert.That(usedAt, Is.Null);
        });
    }

    [Test]
    public async Task The_outbox_payload_holds_only_the_token_id_and_the_cipher()
    {
        var (userId, email) = await AddUserAsync();
        var token = await RequestTokenAsync(userId, email);
        var payload = await QuerySingleAsync<string>(
            $"select o.payload from outbox_messages o join password_reset_tokens t on {TestDatabase.JsonText("o.payload", "reset_token_id")} = {TestDatabase.UuidText("t.id")} where t.user_id = @userId",
            new { userId });
        var keys = JsonDocument.Parse(payload).RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(keys, Is.EqualTo(new[] { "reset_token_id", "token_cipher" }));
            Assert.That(payload, Does.Not.Contain(token));
            Assert.That(payload, Does.Not.Contain(email));
        });
    }

    [TestCase("a\u0000b@example.com")]
    [TestCase("ana\u0001@example.com")]
    public async Task Control_characters_in_the_email_do_not_give_a_server_error(string email)
    {
        using var reset = await _factory.CreateClient().PostAsJsonAsync(RequestPath, new { email });

        Assert.That((int)reset.StatusCode, Is.LessThan(500));
    }

    [Test]
    public async Task A_reset_confirmed_while_an_access_jwt_of_the_user_is_live_answers_204()
    {
        var (userId, email) = await AddUserAsync();
        using var login = await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var token = await RequestTokenAsync(userId, email);

        using var confirmed = await _factory.CreateClient().PostAsJsonAsync(ConfirmPath, new { token, new_password = "Otra-Clave-Segura-2" });

        Assert.That((int)confirmed.StatusCode, Is.EqualTo(204));
    }

    private async Task<string> RequestTokenAsync(Guid userId, string email)
    {
        using var response = await _factory.CreateClient().PostAsJsonAsync(RequestPath, new { email });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        var payload = await QuerySingleAsync<string>(
            $"select o.payload from outbox_messages o join password_reset_tokens t on {TestDatabase.JsonText("o.payload", "reset_token_id")} = {TestDatabase.UuidText("t.id")} " +
            $"where t.user_id = @userId order by t.created_at desc, t.id desc {TestDatabase.First}", new { userId });
        var cipher = JsonDocument.Parse(payload).RootElement.GetProperty("token_cipher").GetString()!;
        return _factory.Services.GetRequiredService<ISecretProtector>().Unprotect(PasswordResetEmail.SecretPurpose, cipher);
    }

    private static async Task<(Guid Id, string Email)> AddUserAsync()
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var id = await IdentityStoreTests.AddUserAsync([], company: null, email, Password);
        return (id, email);
    }

    private static async Task ExecuteAsync(string sql, object parameters)
    {
        await using var connection = TestDatabase.Connect(IdentityDatabase.ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    private static async Task<T> QuerySingleAsync<T>(string sql, object parameters)
    {
        await using var connection = TestDatabase.Connect(IdentityDatabase.ConnectionString);
        return await connection.QuerySingleAsync<T>(sql, parameters);
    }

    private static async Task<IEnumerable<T>> QueryAsync<T>(string sql, object parameters)
    {
        await using var connection = TestDatabase.Connect(IdentityDatabase.ConnectionString);
        return await connection.QueryAsync<T>(sql, parameters);
    }
}

/// <summary>
/// El límite de intentos de la recuperación de contraseña se cuenta por la IP real de la conexión: una cabecera
/// <c>X-Forwarded-For</c> de un cliente que no es un proxy de confianza no lo reinicia.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class PasswordResetRateLimitTests
{
    [TestCase("/api/v1/auth/password-reset/request", """{ "email": "ana@example.com" }""")]
    [TestCase("/api/v1/auth/password-reset/confirm", """{ "token": "abc", "new_password": "Una-Clave-Segura-1" }""")]
    public async Task A_spoofed_x_forwarded_for_from_an_untrusted_client_does_not_reset_the_limit(string path, string body)
    {
        using var baseFactory = new IdentityApiFactory(settings: new Dictionary<string, string?> { ["RateLimit:LoginPerMinute"] = "2" });
        using var factory = baseFactory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddTransient<IStartupFilter, FixedRemoteIp>()));
        using var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Forwarded-For", $"198.51.100.{i + 1}");
            using var response = await client.SendAsync(request);
            statuses.Add(response.StatusCode);
        }

        Assert.That(statuses.Skip(2), Is.All.EqualTo(HttpStatusCode.TooManyRequests), string.Join(",", statuses));
    }

    // Fija la IP de la conexión, como si todas las solicitudes vinieran del mismo cliente.
    private sealed class FixedRemoteIp : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, Func<Task> nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
                return nextMiddleware();
            });
            next(app);
        };
    }
}
