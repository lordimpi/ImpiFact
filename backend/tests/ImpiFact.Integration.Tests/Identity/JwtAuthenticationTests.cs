using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ImpiFact.Application.Abstractions.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Tokens JWT (JSON Web Token) del panel: firma asimétrica ES256 (ECDSA con la curva P-256), identificador de
/// llave (kid) para rotar llaves, vigencia de 15 minutos y claims <c>sub</c>, <c>role</c> y <c>company_id</c>
/// (Arquitectura detallada, secciones 9.2 y 11).
/// </summary>
[TestFixture]
public sealed class JwtAuthenticationTests
{
    private ECDsa _previousKey = null!;
    private IdentityApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi()
    {
        _previousKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _factory = new IdentityApiFactory(settings: new Dictionary<string, string?>
        {
            ["Jwt:PreviousPublicKeys:0"] = _previousKey.ExportSubjectPublicKeyInfoPem(),
        });
    }

    [OneTimeTearDown]
    public void StopApi()
    {
        _factory.Dispose();
        _previousKey.Dispose();
    }

    [Test]
    public async Task An_issued_access_token_authenticates_a_panel_user()
    {
        var userId = Guid.CreateVersion7();
        var companyId = Guid.CreateVersion7();
        var token = _factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(userId, ["owner"], companyId);
        using var client = _factory.CreateClient(token.Value);

        using var response = await client.GetAsync("/test/whoami");
        var whoAmI = await response.Content.ReadFromJsonAsync<TestEndpoints.WhoAmI>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(whoAmI, Is.EqualTo(new TestEndpoints.WhoAmI(companyId, userId, "User")));
        });
    }

    [Test]
    public void The_access_token_is_es256_with_kid_15_minutes_and_the_expected_claims()
    {
        var userId = Guid.CreateVersion7();
        var companyId = Guid.CreateVersion7();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var token = _factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(userId, ["owner", "seller"], companyId);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.Value);

        Assert.Multiple(() =>
        {
            Assert.That(jwt.Alg, Is.EqualTo("ES256"));
            Assert.That(jwt.Kid, Is.EqualTo(TestTokens.KeyId(_factory.SigningKey)));
            Assert.That(jwt.Issuer, Is.EqualTo("impifact"));
            Assert.That(jwt.Audiences, Is.EqualTo(new[] { "impifact-web" }));
            Assert.That(jwt.Subject, Is.EqualTo(userId.ToString()));
            Assert.That(jwt.GetClaim("company_id").Value, Is.EqualTo(companyId.ToString()));
            Assert.That(jwt.Claims.Where(claim => claim.Type == "role").Select(claim => claim.Value), Is.EqualTo(new[] { "owner", "seller" }));
            Assert.That(jwt.ValidTo - jwt.IssuedAt, Is.EqualTo(TimeSpan.FromMinutes(15)));
            Assert.That(token.ExpiresAt, Is.EqualTo(new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero)));
            Assert.That(new DateTimeOffset(jwt.IssuedAt, TimeSpan.Zero), Is.GreaterThanOrEqualTo(before.AddSeconds(-1)));
        });
    }

    [Test]
    public async Task A_platform_admin_token_without_company_has_no_company_in_the_tenant_context()
    {
        var userId = Guid.CreateVersion7();
        var token = _factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(userId, ["platform_admin"], null);
        using var client = _factory.CreateClient(token.Value);

        var whoAmI = await client.GetFromJsonAsync<TestEndpoints.WhoAmI>("/test/whoami");

        Assert.That(whoAmI, Is.EqualTo(new TestEndpoints.WhoAmI(null, userId, "User")));
    }

    [Test]
    public async Task An_expired_token_is_rejected_with_401()
    {
        var token = TestTokens.Sign(_factory.SigningKey, issuedAt: DateTimeOffset.UtcNow.AddMinutes(-30));
        using var client = _factory.CreateClient(token);

        using var response = await client.GetAsync("/test/whoami");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task A_token_signed_with_another_key_is_rejected_even_with_the_right_kid()
    {
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var token = TestTokens.Sign(attackerKey, keyId: TestTokens.KeyId(_factory.SigningKey));
        using var client = _factory.CreateClient(token);

        using var response = await client.GetAsync("/test/whoami");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase("someone-else", "impifact-web")]
    [TestCase("impifact", "another-app")]
    public async Task A_token_for_another_issuer_or_audience_is_rejected(string issuer, string audience)
    {
        var token = TestTokens.Sign(_factory.SigningKey, issuer: issuer, audience: audience);
        using var client = _factory.CreateClient(token);

        using var response = await client.GetAsync("/test/whoami");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task A_token_signed_with_a_previous_key_still_validates_during_rotation()
    {
        var userId = Guid.CreateVersion7();
        var token = TestTokens.Sign(_previousKey, userId: userId);
        using var client = _factory.CreateClient(token);

        var whoAmI = await client.GetFromJsonAsync<TestEndpoints.WhoAmI>("/test/whoami");

        Assert.That(whoAmI, Is.EqualTo(new TestEndpoints.WhoAmI(null, userId, "User")));
    }

    [Test]
    public async Task A_garbage_bearer_token_is_rejected_with_401()
    {
        using var client = _factory.CreateClient("garbage.token.value");

        using var response = await client.GetAsync("/test/whoami");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task The_jwks_document_publishes_the_public_keys_only()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/.well-known/jwks.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var keys = document.RootElement.GetProperty("keys").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(keys.Select(key => key.GetProperty("kid").GetString()),
                Is.EqualTo(new[] { TestTokens.KeyId(_factory.SigningKey), TestTokens.KeyId(_previousKey) }));
            Assert.That(keys.All(key => key.GetProperty("kty").GetString() == "EC" && key.GetProperty("crv").GetString() == "P-256"));
            Assert.That(keys.All(key => key.GetProperty("alg").GetString() == "ES256" && key.GetProperty("use").GetString() == "sig"));
            Assert.That(keys.Any(key => key.TryGetProperty("d", out _)), Is.False, "The private part must never be published.");
        });
    }
}
