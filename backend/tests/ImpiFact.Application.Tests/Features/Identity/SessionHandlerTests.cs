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

    [SetUp]
    public void CreateFakes()
    {
        _clock = new FakeClock();
        _users = new FakeUserStore();
        _hasher = new FakePasswordHasher();
        _refreshStore = new FakeRefreshTokenStore();
        _refreshFactory = new FakeRefreshTokenFactory(_clock);
        _issuer = new FakeAccessTokenIssuer(_clock);
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

    [Test]
    public async Task Presenting_a_revoked_refresh_token_again_revokes_all_active_tokens_of_the_user()
    {
        var user = _users.AddUser("ana@example.com");
        var login = (await Login("ana@example.com", Password)).Value;
        await Refresh(login.RefreshToken);

        var reused = await Refresh(login.RefreshToken);

        Assert.Multiple(() =>
        {
            Assert.That(reused.Error, Is.EqualTo(IdentityErrors.InvalidRefreshToken));
            Assert.That(_refreshStore.RevokedAllFor, Is.EqualTo(new[] { (user.Id, _clock.UtcNow) }));
            Assert.That(_refreshStore.Tokens, Has.Count.EqualTo(2), "No new token is issued for a reused one.");
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
        });
    }

    private Task<Result<SessionTokens>> Login(string email, string password, Guid? companyId = null, string? ip = null, string? agent = null) =>
        new LoginHandler(_users, _hasher, _clock, _issuer, _refreshFactory, _refreshStore)
            .HandleAsync(new LoginCommand(email, password, companyId, ip, agent), CancellationToken.None);

    private Task<Result<SessionTokens>> Refresh(string token, Guid? companyId = null) =>
        new RefreshTokenHandler(_refreshStore, _refreshFactory, _users, _issuer, _clock, NullLogger<RefreshTokenHandler>.Instance)
            .HandleAsync(new RefreshTokenCommand(token, companyId, null, null), CancellationToken.None);
}
