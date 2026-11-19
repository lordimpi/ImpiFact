using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Features.Identity.PasswordReset;
using ImpiFact.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ImpiFact.Integration.Tests.IdentityData;

/// <summary>
/// Recuperación de contraseña de punta a punta, contra PostgreSQL (Arquitectura frontend, sección 16, punto 8): pedir el
/// enlace responde siempre lo mismo (no revela qué correos tienen cuenta), solo se guarda el resumen del token, el correo
/// queda en la bandeja de salida con el token cifrado, y confirmar fija la contraseña nueva una sola vez y cierra todas
/// las sesiones del usuario.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class PasswordResetTests
{
    private const string Password = "Contraseña-Segura-1";

    private const string NewPassword = "Otra-Clave-Segura-2";

    private const string RequestPath = "/api/v1/auth/password-reset/request";

    private const string ConfirmPath = "/api/v1/auth/password-reset/confirm";

    private IdentityDataApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi() => _factory = new IdentityDataApiFactory();

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    [Test]
    public async Task A_request_answers_the_same_202_for_an_active_an_unknown_and_an_inactive_email()
    {
        var (activeId, active) = await AddUserAsync();
        var (inactiveId, inactive) = await AddUserAsync();
        await ExecuteAsync($"update users set is_active = {TestDatabase.Bool(false)} where id = @inactiveId", new { inactiveId });
        var messagesBefore = await QuerySingleAsync<long>("select count(*) from outbox_messages where type = 'SendPasswordResetEmail'", new { });

        using var forActive = await RequestAsync(active.ToUpperInvariant());
        using var forUnknown = await RequestAsync("nadie-" + active);
        using var forInactive = await RequestAsync(inactive);
        var bodies = new[] { forActive, forUnknown, forInactive }.Select(response => response.Content.ReadAsStringAsync().Result).ToList();
        var messagesAfter = await QuerySingleAsync<long>("select count(*) from outbox_messages where type = 'SendPasswordResetEmail'", new { });
        var row = await QuerySingleAsync<(Guid Id, string TokenHash, DateTime ExpiresAt, DateTime? UsedAt, DateTime? RevokedAt)>(
            $"select id, token_hash, {TestDatabase.Utc("expires_at")}, {TestDatabase.Utc("used_at")}, {TestDatabase.Utc("revoked_at")} from password_reset_tokens where user_id = @activeId", new { activeId });
        var inactiveTokens = await QuerySingleAsync<long>("select count(*) from password_reset_tokens where user_id = @inactiveId", new { inactiveId });
        var message = await QuerySingleAsync<(Guid? CompanyId, string Payload)>(
            $"select company_id, payload from outbox_messages where type = 'SendPasswordResetEmail' and {TestDatabase.JsonText("payload", "reset_token_id")} = @id",
            new { id = row.Id.ToString() });
        var token = Decrypt(message.Payload);

        Assert.Multiple(() =>
        {
            Assert.That(new[] { forActive, forUnknown, forInactive }.Select(response => response.StatusCode), Is.All.EqualTo(HttpStatusCode.Accepted));
            Assert.That(bodies.Distinct().Count(), Is.EqualTo(1), "The body never tells whether the email has an account.");
            Assert.That(Message(bodies[0]), Does.StartWith("Si el correo corresponde a una cuenta activa"));
            Assert.That(forActive.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(messagesAfter - messagesBefore, Is.EqualTo(1), "Only the active user gets an email.");
            Assert.That(inactiveTokens, Is.Zero);
            Assert.That(row.TokenHash, Is.EqualTo(CredentialHash.Sha256Hex(token)), "Only the SHA-256 of the token is stored.");
            Assert.That(row.ExpiresAt, Is.EqualTo(DateTime.UtcNow.AddMinutes(30)).Within(TimeSpan.FromMinutes(1)));
            Assert.That(row.UsedAt, Is.Null);
            Assert.That(row.RevokedAt, Is.Null);
            Assert.That(message.CompanyId, Is.Null, "A platform message.");
            Assert.That(message.Payload, Does.Not.Contain(token), "The outbox carries the token encrypted.");
            Assert.That(token, Has.Length.EqualTo(43), "32 random bytes in Base64 URL.");
        });
    }

    [Test]
    public async Task Confirming_sets_the_new_password_closes_every_session_and_the_token_works_once()
    {
        var (userId, email) = await AddUserAsync();
        var firstSession = await RefreshTokenOfLoginAsync(email, Password);
        var secondSession = await RefreshTokenOfLoginAsync(email, Password);
        var stampBefore = await QuerySingleAsync<string>("select security_stamp from users where id = @userId", new { userId });
        var token = await RequestTokenAsync(userId, email);

        using var confirmed = await ConfirmAsync(token, NewPassword);
        var activeSessions = await QuerySingleAsync<long>(
            "select count(*) from refresh_tokens where user_id = @userId and revoked_at is null", new { userId });
        using var oldPassword = await LoginAsync(email, Password);
        using var newPassword = await LoginAsync(email, NewPassword);
        using var firstRefresh = await RefreshAsync(firstSession);
        using var secondRefresh = await RefreshAsync(secondSession);
        using var again = await ConfirmAsync(token, "Una-Tercera-Clave-3");
        var againBody = await again.Content.ReadAsStringAsync();
        var row = await QuerySingleAsync<(DateTime? UsedAt, string Stamp)>(
            $"select {TestDatabase.Utc("t.used_at")}, u.security_stamp from password_reset_tokens t join users u on u.id = t.user_id where t.user_id = @userId", new { userId });

        Assert.Multiple(() =>
        {
            Assert.That(confirmed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(confirmed.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(activeSessions, Is.Zero, "Every refresh token of the user is revoked.");
            Assert.That(oldPassword.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(newPassword.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(firstRefresh.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(secondRefresh.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), "The token works only once.");
            Assert.That(Code(againBody), Is.EqualTo("identity.invalid_reset_token"));
            Assert.That(row.UsedAt, Is.Not.Null);
            Assert.That(row.Stamp, Is.Not.EqualTo(stampBefore), "The security stamp changes with the password.");
        });
    }

    [Test]
    public async Task A_superseded_an_expired_and_an_unknown_token_answer_the_same_422_and_change_nothing()
    {
        var (userId, email) = await AddUserAsync();
        var superseded = await RequestTokenAsync(userId, email);
        var latest = await RequestTokenAsync(userId, email);
        var (expiringId, expiringEmail) = await AddUserAsync();
        var expired = await RequestTokenAsync(expiringId, expiringEmail);
        await ExecuteAsync(
            $"update password_reset_tokens set expires_at = {TestDatabase.MinutesAgo(1)} where user_id = @expiringId", new { expiringId });
        var supersededRow = await QuerySingleAsync<DateTime?>(
            $"select {TestDatabase.Utc("revoked_at")} from password_reset_tokens where token_hash = @hash", new { hash = CredentialHash.Sha256Hex(superseded) });

        using var bySuperseded = await ConfirmAsync(superseded, NewPassword);
        using var byExpired = await ConfirmAsync(expired, NewPassword);
        using var byUnknown = await ConfirmAsync("token-que-no-existe-" + Guid.NewGuid().ToString("N"), NewPassword);
        var bodies = new[] { bySuperseded, byExpired, byUnknown }.Select(response => response.Content.ReadAsStringAsync().Result).ToList();
        using var expiredUserLogin = await LoginAsync(expiringEmail, Password);
        using var byLatest = await ConfirmAsync(latest, NewPassword);

        Assert.Multiple(() =>
        {
            Assert.That(supersededRow, Is.Not.Null, "A new request revokes the unused token of the user.");
            Assert.That(new[] { bySuperseded, byExpired, byUnknown }.Select(response => response.StatusCode),
                Is.All.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(bodies.Select(WithoutRequestIds).Distinct().Count(), Is.EqualTo(1), "The same generic error, without saying why.");
            Assert.That(Code(bodies[0]), Is.EqualTo("identity.invalid_reset_token"));
            Assert.That(Message(bodies[0]), Is.EqualTo("El enlace para restablecer la contraseña no es válido o ya venció. Solicite uno nuevo."));
            Assert.That(expiredUserLogin.StatusCode, Is.EqualTo(HttpStatusCode.OK), "The expired token changed nothing.");
            Assert.That(byLatest.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "The latest token still works.");
        });
    }

    [Test]
    public async Task A_new_password_that_breaks_the_policy_answers_422_and_keeps_the_token_usable()
    {
        var (userId, email) = await AddUserAsync();
        var token = await RequestTokenAsync(userId, email);

        using var tooShort = await ConfirmAsync(token, "corta");
        using var body = JsonDocument.Parse(await tooShort.Content.ReadAsStringAsync());
        using var missing = await _factory.CreateClient().PostAsJsonAsync(ConfirmPath, new { });
        using var missingBody = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
        using var valid = await ConfirmAsync(token, NewPassword);

        Assert.Multiple(() =>
        {
            Assert.That(tooShort.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(body.RootElement.GetProperty("errors").GetProperty("new_password")[0].GetString(),
                Is.EqualTo("La contraseña debe tener al menos 10 caracteres."));
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(missingBody.RootElement.GetProperty("errors").GetProperty("token")[0].GetString(), Is.EqualTo("El token es obligatorio."));
            Assert.That(missingBody.RootElement.GetProperty("errors").GetProperty("new_password")[0].GetString(), Is.EqualTo("La contraseña es obligatoria."));
            Assert.That(valid.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "A rejected password does not spend the token.");
        });
    }

    [Test]
    public async Task Neither_the_token_nor_the_new_password_reaches_the_logs()
    {
        var (userId, email) = await AddUserAsync();
        var token = await RequestTokenAsync(userId, email);
        using var rejected = await ConfirmAsync(token, "corta");
        using var confirmed = await ConfirmAsync(token, NewPassword);
        using var reused = await ConfirmAsync(token, NewPassword);

        Assert.Multiple(() =>
        {
            Assert.That(confirmed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(_factory.Logs.Entries, Has.Some.Contains("reset token"), "The flow was logged; otherwise this test proves nothing.");
            Assert.That(_factory.Logs.Entries, Has.None.Contains(token));
            Assert.That(_factory.Logs.Entries, Has.None.Contains(NewPassword));
        });
    }

    [Test]
    public async Task The_worker_reads_the_recipient_of_a_token_from_the_tables()
    {
        var (userId, email) = await AddUserAsync();
        await RequestTokenAsync(userId, email);
        var tokenId = await QuerySingleAsync<Guid>("select id from password_reset_tokens where user_id = @userId", new { userId });

        await using var scope = IdentityDatabase.Open(companyId: null);
        var store = scope.ServiceProvider.GetRequiredService<ImpiFact.Application.Abstractions.Identity.IPasswordResetTokenStore>();
        var recipient = await store.FindRecipientAsync(tokenId, CancellationToken.None);
        var missing = await store.FindRecipientAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(recipient?.Email, Is.EqualTo(email));
            Assert.That(recipient?.Name, Is.EqualTo("Usuario de prueba"));
            Assert.That(recipient?.UserIsActive, Is.True);
            Assert.That(recipient?.Token.UserId, Is.EqualTo(userId));
            Assert.That(recipient?.Token.IsUsableAt(DateTimeOffset.UtcNow), Is.True);
            Assert.That(recipient?.ToString(), Does.Not.Contain(email));
            Assert.That(missing, Is.Null);
        });
    }

    private async Task<HttpResponseMessage> RequestAsync(string email) =>
        await _factory.CreateClient().PostAsJsonAsync(RequestPath, new { email });

    private async Task<HttpResponseMessage> ConfirmAsync(string token, string newPassword) =>
        await _factory.CreateClient().PostAsJsonAsync(ConfirmPath, new { token, new_password = newPassword });

    private async Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refresh_token = refreshToken });

    private async Task<string> RefreshTokenOfLoginAsync(string email, string password)
    {
        using var response = await LoginAsync(email, password);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("refresh_token").GetString()!;
    }

    /// <summary>Pide el enlace y saca el token del mensaje de la bandeja de salida, como haría el Worker al armar el correo.</summary>
    private async Task<string> RequestTokenAsync(Guid userId, string email)
    {
        using var response = await RequestAsync(email);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        var payload = await QuerySingleAsync<string>(
            $"select o.payload from outbox_messages o join password_reset_tokens t on {TestDatabase.JsonText("o.payload", "reset_token_id")} = {TestDatabase.UuidText("t.id")} " +
            $"where t.user_id = @userId order by t.created_at desc, t.id desc {TestDatabase.First}",
            new { userId });
        return Decrypt(payload);
    }

    private string Decrypt(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var cipher = document.RootElement.GetProperty("token_cipher").GetString()!;
        return _factory.Services.GetRequiredService<ISecretProtector>().Unprotect(PasswordResetEmail.SecretPurpose, cipher);
    }

    private static async Task<(Guid Id, string Email)> AddUserAsync()
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var id = await IdentityStoreTests.AddUserAsync([], company: null, email, Password);
        return (id, email);
    }

    /// <summary>
    /// El cuerpo sin <c>traceId</c> ni <c>correlation_id</c>, que identifican cada solicitud: lo demás debe ser idéntico.
    /// </summary>
    private static string WithoutRequestIds(string body)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(body)!.AsObject();
        node.Remove("traceId");
        node.Remove("correlation_id");
        return node.ToJsonString();
    }

    private static string? Code(string body) => JsonDocument.Parse(body).RootElement.GetProperty("code").GetString();

    private static string? Message(string body) => JsonDocument.Parse(body).RootElement.GetProperty("message").GetString();

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
}
