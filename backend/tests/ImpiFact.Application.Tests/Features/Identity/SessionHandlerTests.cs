using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Identity;
using ImpiFact.Application.Features.Identity.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImpiFact.Application.Tests.Features.Identity;

/// <summary>
/// Inicio de sesión y renovación (Arquitectura detallada, sección 9.2): error genérico para correo o contraseña
/// equivocados, empresa activa, rotación del token de renovación y revocación de todos los tokens si uno revocado
/// se presenta otra vez.
/// </summary>
[TestFixture]
public sealed class SessionHandlerTests
{
    private const string Password = "Secreta-123";

    private FakeClock _clock = null!;
    private FakeUserStore _users = null!;
    private FakePasswordHasher _hasher = null!;
    private FakeRefreshTokenStore _refreshStore = null!;
    private FakeRefreshTokenFactory _refreshFactory = null!;
    private FakeAccessTokenIssuer _issuer = null!;
    private FakeSessionPolicy _policy = null!;

    [SetUp]
    public void CreateFakes()
    {
        _clock = new FakeClock();
        _users = new FakeUserStore();
        _hasher = new FakePasswordHasher();
        _refreshStore = new FakeRefreshTokenStore();
        _refreshFactory = new FakeRefreshTokenFactory(_clock);
        _issuer = new FakeAccessTokenIssuer(_clock);
        _policy = new FakeSessionPolicy();
    }

    [Test]
    public async Task A_valid_login_returns_both_tokens_and_stores_only_the_refresh_hash()
    {
        var companyId = Guid.CreateVersion7();
        var user = _users.AddUser("ana@example.com");
        _users.Join(user.Id, companyId, "owner");

        var result = await Login("  Ana@Example.COM ", Password, companyId: null, ip: "203.0.113.9", agent: "Firefox");

        Assert.That(result.IsSuccess, Is.True);
        var tokens = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(tokens.AccessToken, Is.EqualTo("jwt-1"));
            Assert.That(tokens.AccessTokenExpiresAt, Is.EqualTo(_clock.UtcNow.AddMinutes(15)));
            Assert.That(tokens.RefreshToken, Is.EqualTo("refresh-1"));
            Assert.That(tokens.RefreshTokenExpiresAt, Is.EqualTo(_clock.UtcNow.AddDays(14)));
            Assert.That(tokens.CompanyId, Is.EqualTo(companyId), "A user with one company gets it as active company.");
            Assert.That(tokens.Roles, Is.EqualTo(new[] { "owner" }));
            Assert.That(tokens.Companies, Is.EqualTo(new[] { companyId }));
            Assert.That(tokens.TokenType, Is.EqualTo("Bearer"));
            Assert.That(_refreshStore.Tokens, Has.Count.EqualTo(1));
            Assert.That(_refreshStore.Tokens[0].TokenHash, Is.EqualTo("sha:refresh-1"));
            Assert.That(_refreshStore.Tokens[0].UserId, Is.EqualTo(user.Id));
            Assert.That(_refreshStore.Tokens[0].CreatedIp, Is.EqualTo("203.0.113.9"));
            Assert.That(_refreshStore.Tokens[0].UserAgent, Is.EqualTo("Firefox"));
            Assert.That(tokens.ToString(), Does.Not.Contain("jwt-1").And.Not.Contain("refresh-1"));
        });
    }

    [Test]
    public async Task Unknown_email_and_wrong_password_give_the_same_error_and_both_verify_a_hash()
    {
        _users.AddUser("ana@example.com");

        var unknown = await Login("nadie@example.com", Password);
        var wrong = await Login("ana@example.com", "otra-clave");

        Assert.Multiple(() =>
        {
            Assert.That(unknown.Error, Is.EqualTo(IdentityErrors.InvalidCredentials));
            Assert.That(wrong.Error, Is.EqualTo(IdentityErrors.InvalidCredentials));
            Assert.That(IdentityErrors.InvalidCredentials.Type, Is.EqualTo(ErrorType.Unauthorized));
            Assert.That(_hasher.Verified, Is.EqualTo(new[] { null, "hash:" + Password }), "The unknown email still runs a verification.");
            Assert.That(_refreshStore.Tokens, Is.Empty);
        });
    }

    [Test]
    public async Task An_inactive_or_locked_user_gets_the_generic_error_even_with_the_right_password()
    {
        _users.AddUser("inactivo@example.com", isActive: false);
        _users.AddUser("bloqueado@example.com", lockoutEnd: _clock.UtcNow.AddMinutes(5));

        var inactive = await Login("inactivo@example.com", Password);
        var locked = await Login("bloqueado@example.com", Password);

        Assert.Multiple(() =>
        {
            Assert.That(inactive.Error, Is.EqualTo(IdentityErrors.InvalidCredentials));
            Assert.That(locked.Error, Is.EqualTo(IdentityErrors.InvalidCredentials));
        });
    }

    [Test]
    public async Task A_hash_with_old_parameters_is_saved_again_after_a_valid_login()
    {
        var user = _users.AddUser("ana@example.com", passwordHash: "old:" + Password);

        var result = await Login("ana@example.com", Password);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_users.UpdatedHashes[user.Id], Is.EqualTo("hash:" + Password));
        });
    }

    [Test]
    public async Task A_requested_company_must_be_one_of_the_user_companies()
    {
        var mine = Guid.CreateVersion7();
        var user = _users.AddUser("ana@example.com");
        _users.Join(user.Id, mine, "seller");

        var other = await Login("ana@example.com", Password, companyId: Guid.CreateVersion7());
        var own = await Login("ana@example.com", Password, companyId: mine);

        Assert.Multiple(() =>
        {
            Assert.That(other.Error, Is.EqualTo(IdentityErrors.CompanyNotAllowed));
            Assert.That(own.Value.CompanyId, Is.EqualTo(mine));
            Assert.That(own.Value.Roles, Is.EqualTo(new[] { "seller" }));
        });
    }

    [Test]
    public async Task A_user_with_several_companies_or_platform_roles_gets_no_active_company_by_default()
    {
        var several = _users.AddUser("varias@example.com");
        _users.Join(several.Id, Guid.CreateVersion7(), "owner");
        _users.Join(several.Id, Guid.CreateVersion7(), "seller");
        var admin = _users.AddUser("admin@example.com");
        _users.Grant(admin.Id, "platform_admin");
        _users.Join(admin.Id, Guid.CreateVersion7(), "owner");

        var first = await Login("varias@example.com", Password);
        var second = await Login("admin@example.com", Password);

        Assert.Multiple(() =>
        {
            Assert.That(first.Value.CompanyId, Is.Null);
            Assert.That(first.Value.Roles, Is.Empty);
            Assert.That(first.Value.Companies, Has.Count.EqualTo(2));
            Assert.That(second.Value.CompanyId, Is.Null);
            Assert.That(second.Value.Roles, Is.EqualTo(new[] { "platform_admin" }));
        });
    }

    [Test]
    public async Task A_refresh_rotates_the_token_and_revokes_the_used_one()
    {
        var companyId = Guid.CreateVersion7();
        var user = _users.AddUser("ana@example.com");
        _users.Join(user.Id, companyId, "owner");
        var login = (await Login("ana@example.com", Password)).Value;
        _clock.UtcNow = _clock.UtcNow.AddMinutes(20);

        var refreshed = await Refresh(login.RefreshToken);

        Assert.That(refreshed.IsSuccess, Is.True);
        var old = _refreshStore.Tokens.Single(token => token.TokenHash == "sha:refresh-1");
        var replacement = _refreshStore.Tokens.Single(token => token.TokenHash == "sha:refresh-2");
        Assert.Multiple(() =>
        {
            Assert.That(refreshed.Value.RefreshToken, Is.EqualTo("refresh-2"));
            Assert.That(refreshed.Value.AccessToken, Is.EqualTo("jwt-2"));
            Assert.That(refreshed.Value.CompanyId, Is.EqualTo(companyId));
            Assert.That(old.RevokedAt, Is.EqualTo(_clock.UtcNow));
            Assert.That(old.ReplacedBy, Is.EqualTo(replacement.Id));
            Assert.That(replacement.RevokedAt, Is.Null);
            Assert.That(_refreshStore.RevokedAllFor, Is.Empty);
        });
    }

    // Fuera del período de gracia (30 segundos desde la rotación), un token rotado que vuelve es robo.
    [Test]
    public async Task Presenting_a_revoked_refresh_token_again_revokes_all_active_tokens_of_the_user()
    {
        var user = _users.AddUser("ana@example.com");
        var login = (await Login("ana@example.com", Password)).Value;
        await Refresh(login.RefreshToken);
        _clock.UtcNow = _clock.UtcNow.AddSeconds(30);

        var reused = await Refresh(login.RefreshToken);

        Assert.Multiple(() =>
        {
            Assert.That(reused.Error, Is.EqualTo(IdentityErrors.InvalidRefreshToken));
            Assert.That(_refreshStore.RevokedAllFor, Is.EqualTo(new[] { (user.Id, _clock.UtcNow) }));
            Assert.That(_refreshStore.Tokens, Has.Count.EqualTo(2), "No new token is issued for a reused one.");
        });
    }

    [Test]
    public async Task A_reused_refresh_token_revokes_every_session_even_when_the_client_aborted_the_request()
    {
        var user = _users.AddUser("ana@example.com");
        var login = (await Login("ana@example.com", Password)).Value;
        await Refresh(login.RefreshToken);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);

        // La pestaña del usuario se recargó justo al presentar el token viejo: la petición llega cancelada.
        var reused = await Refresh(login.RefreshToken, ct: new CancellationToken(canceled: true));

        Assert.Multiple(() =>
        {
            Assert.That(reused.Error, Is.EqualTo(IdentityErrors.InvalidRefreshToken));
            Assert.That(_refreshStore.RevokedAllFor, Is.EqualTo(new[] { (user.Id, _clock.UtcNow) }),
                "The response to a stolen token must finish even if nobody waits for the answer.");
        });
    }

    // La revocación por robo tiene su propio plazo (no usa la cancelación de la petición). Si ese plazo se cumple porque
    // la base no contesta, la API responde el 503 de una base caída, no un 500.
    [Test]
    public async Task A_theft_revocation_that_runs_out_of_time_reports_the_credential_store_as_unavailable()
    {
        _users.AddUser("ana@example.com");
        var login = (await Login("ana@example.com", Password)).Value;
        await Refresh(login.RefreshToken);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        _refreshStore.RevokeAllHangs = true;
        var started = DateTimeOffset.UtcNow;

        var exception = Assert.ThrowsAsync<CredentialStoreUnavailableException>(() => Refresh(login.RefreshToken));
        var elapsed = DateTimeOffset.UtcNow - started;

        Assert.Multiple(() =>
        {
            Assert.That(exception?.InnerException, Is.InstanceOf<OperationCanceledException>());
            Assert.That(elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "The policy deadline applies, not a fixed one.");
            Assert.That(_refreshStore.RevokedAllFor, Is.Empty);
            Assert.That(_refreshStore.Tokens, Has.Count.EqualTo(2), "No new token is issued for a reused one.");
        });
    }

    // Respuesta perdida: el navegador cortó la renovación justo después de que el servidor rotara el token, se quedó con
    // la cookie vieja y la presenta otra vez. Dentro del período de gracia y con el reemplazo sin usar, se revoca ese
    // reemplazo y se entrega un par nuevo de la misma sesión, en lugar de cerrar todas las sesiones del usuario.
    [Test]
    public async Task A_rotated_token_presented_again_within_the_grace_period_with_an_unused_replacement_gets_a_fresh_pair()
    {
        var companyId = Guid.CreateVersion7();
        var user = _users.AddUser("ana@example.com");
        _users.Join(user.Id, companyId, "owner");
        var login = (await Login("ana@example.com", Password)).Value;
        _clock.UtcNow = _clock.UtcNow.AddMinutes(20);
        var rotatedAt = _clock.UtcNow;
        await Refresh(login.RefreshToken);
        _clock.UtcNow = rotatedAt.AddSeconds(29);

        var recovered = await Refresh(login.RefreshToken, ip: "203.0.113.7", agent: "Firefox");

        var presented = _refreshStore.Tokens.Single(token => token.TokenHash == "sha:refresh-1");
        var lost = _refreshStore.Tokens.Single(token => token.TokenHash == "sha:refresh-2");
        var fresh = _refreshStore.Tokens.Single(token => token.TokenHash == "sha:refresh-3");
        Assert.Multiple(() =>
        {
            Assert.That(recovered.IsSuccess, Is.True);
            Assert.That(recovered.Value.RefreshToken, Is.EqualTo("refresh-3"));
            Assert.That(recovered.Value.AccessToken, Is.EqualTo("jwt-3"));
            Assert.That(recovered.Value.CompanyId, Is.EqualTo(companyId));
            Assert.That(_issuer.Issued[^1].UserId, Is.EqualTo(user.Id));
            Assert.That(presented.RevokedAt, Is.EqualTo(rotatedAt), "The presented token keeps its first rotation.");
            Assert.That(presented.ReplacedBy, Is.EqualTo(lost.Id));
            Assert.That(lost.RevokedAt, Is.EqualTo(_clock.UtcNow), "The replacement the browser never received is revoked.");
            Assert.That(lost.ReplacedBy, Is.EqualTo(fresh.Id));
            Assert.That(fresh.RevokedAt, Is.Null);
            Assert.That(fresh.UserId, Is.EqualTo(user.Id));
            Assert.That(fresh.CreatedIp, Is.EqualTo("203.0.113.7"));
            Assert.That(fresh.UserAgent, Is.EqualTo("Firefox"));
            Assert.That(_refreshStore.Tokens.Count(token => token.RevokedAt is null), Is.EqualTo(1));
            Assert.That(_refreshStore.RevokedAllFor, Is.Empty, "A lost response is not a theft.");
        });
    }

    [Test]
    public async Task A_rotated_token_presented_again_when_the_grace_period_is_over_is_still_a_theft()
    {
        var user = _users.AddUser("ana@example.com");
        var login = (await Login("ana@example.com", Password)).Value;
        await Refresh(login.RefreshToken);
        _clock.UtcNow = _clock.UtcNow.AddSeconds(30);

        var reused = await Refresh(login.RefreshToken);

        Assert.Multiple(() =>
        {
            Assert.That(reused.Error, Is.EqualTo(IdentityErrors.InvalidRefreshToken));
            Assert.That(_refreshStore.RevokedAllFor, Is.EqualTo(new[] { (user.Id, _clock.UtcNow) }));
            Assert.That(_refreshStore.Tokens, Has.Count.EqualTo(2), "No new token is issued for a reused one.");
            Assert.That(_refreshStore.Tokens.Single(token => token.TokenHash == "sha:refresh-2").RevokedAt, Is.Null,
                "Only the theft response (RevokeAllActiveForUserAsync) ends the replacement.");
        });
    }

    [Test]
    public async Task A_rotated_token_whose_replacement_was_already_used_is_a_theft_even_within_the_grace_period()
    {
        var user = _users.AddUser("ana@example.com");
        var login = (await Login("ana@example.com", Password)).Value;
        var replacement = (await Refresh(login.RefreshToken)).Value;
        await Refresh(replacement.RefreshToken);
        _clock.UtcNow = _clock.UtcNow.AddSeconds(5);

        var reused = await Refresh(login.RefreshToken);

        Assert.Multiple(() =>
        {
            Assert.That(reused.Error, Is.EqualTo(IdentityErrors.InvalidRefreshToken));
            Assert.That(_refreshStore.RevokedAllFor, Is.EqualTo(new[] { (user.Id, _clock.UtcNow) }));
            Assert.That(_refreshStore.Tokens, Has.Count.EqualTo(3), "No new token is issued for a reused one.");
        });
    }

    // Dentro del período de gracia, decidir entre respuesta perdida y robo exige leer el reemplazo. Esa lectura tampoco
    // usa la cancelación de la petición: si el dueño presenta su token viejo y su pestaña se recarga justo entonces, la
    // respuesta de robo debe terminar igual.
    [Test]
    public async Task A_theft_within_the_grace_period_revokes_every_session_even_when_the_client_aborted_the_request()
    {
        var user = _users.AddUser("ana@example.com");
        var login = (await Login("ana@example.com", Password)).Value;
        var replacement = (await Refresh(login.RefreshToken)).Value;
        await Refresh(replacement.RefreshToken);
        _clock.UtcNow = _clock.UtcNow.AddSeconds(5);

        var reused = await Refresh(login.RefreshToken, ct: new CancellationToken(canceled: true));

        Assert.Multiple(() =>
        {
            Assert.That(reused.Error, Is.EqualTo(IdentityErrors.InvalidRefreshToken));
            Assert.That(_refreshStore.RevokedAllFor, Is.EqualTo(new[] { (user.Id, _clock.UtcNow) }));
        });
    }

    [Test]
    public async Task A_token_closed_by_a_logout_is_a_theft_even_within_the_grace_period()
    {
        var user = _users.AddUser("ana@example.com");
        var login = (await Login("ana@example.com", Password)).Value;
        await Logout(login.RefreshToken);
        _clock.UtcNow = _clock.UtcNow.AddSeconds(5);

        var reused = await Refresh(login.RefreshToken);

        Assert.Multiple(() =>
        {
            Assert.That(reused.Error, Is.EqualTo(IdentityErrors.InvalidRefreshToken));
            Assert.That(_refreshStore.RevokedAllFor, Is.EqualTo(new[] { (user.Id, _clock.UtcNow) }));
            Assert.That(_refreshStore.Tokens, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task A_grace_period_of_zero_turns_the_recovery_off()
    {
        var user = _users.AddUser("ana@example.com");
        var login = (await Login("ana@example.com", Password)).Value;
        await Refresh(login.RefreshToken);
        _policy.RotationGracePeriod = TimeSpan.Zero;

        var reused = await Refresh(login.RefreshToken);

        Assert.Multiple(() =>
        {
            Assert.That(reused.Error, Is.EqualTo(IdentityErrors.InvalidRefreshToken));
            Assert.That(_refreshStore.RevokedAllFor, Is.EqualTo(new[] { (user.Id, _clock.UtcNow) }));
        });
    }

    [Test]
    public async Task A_second_recovery_with_the_same_token_is_a_theft()
    {
        var user = _users.AddUser("ana@example.com");
        var login = (await Login("ana@example.com", Password)).Value;
        await Refresh(login.RefreshToken);
        var first = await Refresh(login.RefreshToken);

        var second = await Refresh(login.RefreshToken);

        Assert.Multiple(() =>
        {
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(second.Error, Is.EqualTo(IdentityErrors.InvalidRefreshToken), "The replacement is no longer unused.");
            Assert.That(_refreshStore.RevokedAllFor, Is.EqualTo(new[] { (user.Id, _clock.UtcNow) }));
            Assert.That(_refreshStore.Tokens, Has.Count.EqualTo(3));
        });
    }

    // Dos solicitudes simultáneas con el mismo token viejo: las dos leen el reemplazo sin usar, pero solo una lo puede
    // reclamar (RevokeAsync; en la base, la versión de fila). La otra recibe SessionIssuer.AlreadyUsed y se trata como
    // robo: nunca quedan dos pares vivos.
    [Test]
    public async Task Two_simultaneous_recoveries_with_the_same_token_never_both_get_a_pair()
    {
        var user = _users.AddUser("ana@example.com");
        var login = (await Login("ana@example.com", Password)).Value;
        await Refresh(login.RefreshToken);
        var unusedReplacement = _refreshStore.Tokens.Single(token => token.TokenHash == "sha:refresh-2");
        var first = await Refresh(login.RefreshToken);
        _refreshStore.StaleFindById = id => id == unusedReplacement.Id ? unusedReplacement : null;

        var second = await Refresh(login.RefreshToken);

        Assert.Multiple(() =>
        {
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(second.Error, Is.EqualTo(IdentityErrors.InvalidRefreshToken));
            Assert.That(_refreshStore.RevokedAllFor, Is.EqualTo(new[] { (user.Id, _clock.UtcNow) }));
            Assert.That(_refreshStore.Tokens, Has.Count.EqualTo(3), "The loser issues no token.");
        });
    }

    [Test]
    public async Task An_unknown_or_expired_refresh_token_or_an_inactive_user_gets_the_generic_error()
    {
        var user = _users.AddUser("ana@example.com");
        var login = (await Login("ana@example.com", Password)).Value;

        var unknown = await Refresh("refresh-que-no-existe");
        _clock.UtcNow = _clock.UtcNow.AddDays(15);
        var expired = await Refresh(login.RefreshToken);

        Assert.Multiple(() =>
        {
            Assert.That(unknown.Error, Is.EqualTo(IdentityErrors.InvalidRefreshToken));
            Assert.That(expired.Error, Is.EqualTo(IdentityErrors.InvalidRefreshToken));
            Assert.That(_refreshStore.RevokedAllFor, Is.Empty);
        });

        _clock.UtcNow = _clock.UtcNow.AddDays(-15);
        _users.Users[0] = user with { IsActive = false };
        var inactive = await Refresh(login.RefreshToken);
        Assert.That(inactive.Error, Is.EqualTo(IdentityErrors.InvalidRefreshToken));
    }

    [Test]
    public async Task A_refresh_can_switch_to_another_company_of_the_user_only()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var user = _users.AddUser("ana@example.com");
        _users.Join(user.Id, first, "owner");
        _users.Join(user.Id, second, "seller");
        var login = (await Login("ana@example.com", Password, companyId: first)).Value;

        var foreign = await Refresh(login.RefreshToken, companyId: Guid.CreateVersion7());
        var switched = await Refresh(login.RefreshToken, companyId: second);

        Assert.Multiple(() =>
        {
            Assert.That(foreign.Error, Is.EqualTo(IdentityErrors.CompanyNotAllowed));
            Assert.That(switched.Value.CompanyId, Is.EqualTo(second));
            Assert.That(switched.Value.Roles, Is.EqualTo(new[] { "seller" }));
        });
    }

    [Test]
    public void The_commands_do_not_print_their_secrets()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new LoginCommand("a@b.co", Password, null, null, null).ToString(), Does.Not.Contain(Password));
            Assert.That(new RefreshTokenCommand("refresh-1", null, null, null).ToString(), Does.Not.Contain("refresh-1"));
            Assert.That(new LogoutCommand("refresh-1").ToString(), Does.Not.Contain("refresh-1"));
        });
    }

    [Test]
    public async Task A_logout_revokes_the_presented_token_and_leaves_the_other_sessions_alive()
    {
        var user = _users.AddUser("ana@example.com");
        var thisDevice = (await Login("ana@example.com", Password)).Value;
        await Login("ana@example.com", Password);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(5);

        var loggedOut = await Logout(thisDevice.RefreshToken);
        var revoked = _refreshStore.Tokens.Single(token => token.TokenHash == "sha:refresh-1");
        var otherDevice = _refreshStore.Tokens.Single(token => token.TokenHash == "sha:refresh-2");
        var refreshAfterLogout = await Refresh(thisDevice.RefreshToken);

        Assert.Multiple(() =>
        {
            Assert.That(loggedOut.IsSuccess, Is.True);
            Assert.That(loggedOut.Value.Revoked, Is.True);
            Assert.That(revoked.RevokedAt, Is.EqualTo(_clock.UtcNow));
            Assert.That(revoked.ReplacedBy, Is.Null, "A logout does not hand the session to another token.");
            Assert.That(otherDevice.RevokedAt, Is.Null, "A logout ends only the session that presented the token.");
            Assert.That(refreshAfterLogout.Error, Is.EqualTo(IdentityErrors.InvalidRefreshToken));
            Assert.That(user.Id, Is.EqualTo(revoked.UserId));
        });
    }

    [Test]
    public async Task A_logout_with_an_unknown_or_already_revoked_token_succeeds_without_revoking_other_sessions()
    {
        _users.AddUser("ana@example.com");
        var login = (await Login("ana@example.com", Password)).Value;
        await Logout(login.RefreshToken);

        var again = await Logout(login.RefreshToken);
        var unknown = await Logout("refresh-que-no-existe");

        Assert.Multiple(() =>
        {
            Assert.That(again.IsSuccess, Is.True);
            Assert.That(again.Value.Revoked, Is.False);
            Assert.That(unknown.IsSuccess, Is.True);
            Assert.That(unknown.Value.Revoked, Is.False);
            Assert.That(_refreshStore.RevokedAllFor, Is.Empty, "Logging out twice is not treated as a stolen token.");
        });
    }

    private Task<Result<SessionTokens>> Login(string email, string password, Guid? companyId = null, string? ip = null, string? agent = null) =>
        new LoginHandler(_users, _hasher, _clock, _issuer, _refreshFactory, _refreshStore)
            .HandleAsync(new LoginCommand(email, password, companyId, ip, agent), CancellationToken.None);

    private Task<Result<SessionTokens>> Refresh(
        string token, Guid? companyId = null, CancellationToken ct = default, string? ip = null, string? agent = null) =>
        new RefreshTokenHandler(_refreshStore, _refreshFactory, _users, _issuer, _clock, _policy, NullLogger<RefreshTokenHandler>.Instance)
            .HandleAsync(new RefreshTokenCommand(token, companyId, ip, agent), ct);

    private Task<Result<LoggedOut>> Logout(string token) =>
        new LogoutHandler(_refreshStore, _refreshFactory, _clock, NullLogger<LogoutHandler>.Instance)
            .HandleAsync(new LogoutCommand(token), CancellationToken.None);
}
