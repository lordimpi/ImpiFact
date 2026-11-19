using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Npgsql;

namespace ImpiFact.Integration.Tests.IdentityData;

/// <summary>
/// El cierre de sesión y la revocación por un token de renovación reusado tienen su propio plazo (no usan la cancelación
/// de la petición). Si ese plazo se cumple porque la base no termina la escritura, la API responde el mismo 503
/// (<c>identity.store_unavailable</c>) que ante una base caída, no un 500. Para que la base no termine, otra conexión
/// bloquea la fila del token con <c>select … for update</c>; el plazo de esta API es de 1 segundo en lugar de 10.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class SessionRevocationTimeoutTests
{
    private const string Password = "Contraseña-Segura-1";
    private const string CookieName = "impifact_refresh";

    private IdentityDataApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi() => _factory = new IdentityDataApiFactory(services =>
        services.AddSingleton<ISessionPolicy>(new ShortRevocation()));

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    [Test]
    public async Task A_logout_whose_revocation_runs_out_of_time_answers_503_and_keeps_the_cookie()
    {
        var (_, email) = await AddUserAsync();
        var cookie = await PanelLoginAsync(email);
        await using var lockHolder = await LockAsync("select id from refresh_tokens where token_hash = @hash for update",
            new { hash = CredentialHash.Sha256Hex(cookie) });

        using var response = await Client().SendAsync(PanelRequest("/api/v1/auth/logout", cookie));
        var text = await response.Content.ReadAsStringAsync();
        await lockHolder.RollbackAsync();
        var revokedAt = await QuerySingleAsync<DateTime?>(
            "select revoked_at from refresh_tokens where token_hash = @hash", new { hash = CredentialHash.Sha256Hex(cookie) });

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(response.Headers.RetryAfter?.Delta, Is.Not.Null);
            Assert.That(Code(text), Is.EqualTo("identity.store_unavailable"));
            Assert.That(response.Headers.Contains(HeaderNames.SetCookie), Is.False, "The panel keeps its cookie and can try again.");
            Assert.That(revokedAt, Is.Null, "Nothing was revoked: the panel must log out again.");
        });
    }

    [Test]
    public async Task A_theft_revocation_that_runs_out_of_time_answers_503_and_keeps_the_cookie()
    {
        var (userId, email) = await AddUserAsync();
        var original = await PanelLoginAsync(email);
        using var rotated = await Client().SendAsync(PanelRequest("/api/v1/auth/refresh", original));
        var replacement = await ReadCookieAsync(rotated);
        // Se usa también el reemplazo: así el token viejo ya no es una respuesta perdida, sino un reúso.
        using var used = await Client().SendAsync(PanelRequest("/api/v1/auth/refresh", replacement));
        await using var lockHolder = await LockAsync(
            "select id from refresh_tokens where user_id = @userId and revoked_at is null for update", new { userId });

        using var reused = await Client().SendAsync(PanelRequest("/api/v1/auth/refresh", original));
        var text = await reused.Content.ReadAsStringAsync();
        await lockHolder.RollbackAsync();
        var active = await QuerySingleAsync<long>(
            "select count(*) from refresh_tokens where user_id = @userId and revoked_at is null", new { userId });

        Assert.Multiple(() =>
        {
            Assert.That(rotated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(used.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(reused.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(reused.Headers.RetryAfter?.Delta, Is.Not.Null);
            Assert.That(Code(text), Is.EqualTo("identity.store_unavailable"));
            Assert.That(reused.Headers.Contains(HeaderNames.SetCookie), Is.False);
            Assert.That(active, Is.EqualTo(1), "The revocation did not finish, so the last session is still alive.");
        });

        // Con la base libre, el mismo reúso vuelve a ser la respuesta de siempre: 401 y todas las sesiones revocadas.
        using var again = await Client().SendAsync(PanelRequest("/api/v1/auth/refresh", original));
        var activeAfter = await QuerySingleAsync<long>(
            "select count(*) from refresh_tokens where user_id = @userId and revoked_at is null", new { userId });
        Assert.Multiple(() =>
        {
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(activeAfter, Is.Zero);
        });
    }

    private sealed class ShortRevocation : ISessionPolicy
    {
        public TimeSpan RevocationTimeout => TimeSpan.FromSeconds(1);

        public TimeSpan RotationGracePeriod => TimeSpan.FromSeconds(30);
    }

    private static async Task<(Guid Id, string Email)> AddUserAsync()
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var id = await IdentityStoreTests.AddUserAsync([], company: null, email, Password);
        return (id, email);
    }

    private async Task<string> PanelLoginAsync(string email)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email, password = Password }) };
        request.Headers.Add("X-Client", "panel");
        using var response = await Client().SendAsync(request);
        return await ReadCookieAsync(response);
    }

    private static Task<string> ReadCookieAsync(HttpResponseMessage response)
    {
        Assert.That(response.Headers.TryGetValues(HeaderNames.SetCookie, out var values), Is.True, "The response sets no cookie.");
        return Task.FromResult(SetCookieHeaderValue.ParseList(values!.ToList()).Single(cookie => cookie.Name.Value == CookieName).Value.Value!);
    }

    private HttpClient Client() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });

    private static HttpRequestMessage PanelRequest(string path, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-Client", "panel");
        request.Headers.Add(HeaderNames.Cookie, $"{CookieName}={cookie}");
        return request;
    }

    private static string? Code(string body) => JsonDocument.Parse(body).RootElement.GetProperty("code").GetString();

    /// <summary>Otra conexión con una transacción abierta que bloquea las filas de la consulta hasta revertirla.</summary>
    private static async Task<LockHolder> LockAsync(string sql, object parameters)
    {
        var connection = new NpgsqlConnection(IdentityDatabase.ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(sql, parameters, transaction);
        return new LockHolder(connection, transaction);
    }

    private static async Task<T> QuerySingleAsync<T>(string sql, object parameters)
    {
        await using var connection = new NpgsqlConnection(IdentityDatabase.ConnectionString);
        return await connection.QuerySingleAsync<T>(sql, parameters);
    }

    private sealed class LockHolder(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        private bool _released;

        public async Task RollbackAsync()
        {
            if (!_released)
            {
                _released = true;
                await transaction.RollbackAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await RollbackAsync();
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
