using System.Net;
using ImpiFact.Application.Abstractions.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Los JWT (JSON Web Token) del panel llevan un claim <c>scope</c> por cada alcance que dan los roles del usuario,
/// así las mismas políticas por alcance de las API keys sirven para los usuarios.
/// </summary>
[TestFixture]
public sealed class UserScopeTests
{
    private IdentityApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi() => _factory = new IdentityApiFactory();

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    [Test]
    public void An_owner_token_carries_the_owner_scopes()
    {
        var token = Issue(["owner"], Guid.CreateVersion7());

        var scopes = new JsonWebTokenHandler().ReadJsonWebToken(token).Claims
            .Where(claim => claim.Type == "scope").Select(claim => claim.Value);

        Assert.That(scopes, Is.EqualTo(new[] { ApiScopes.DocumentsRead, ApiScopes.DocumentsWrite, ApiScopes.ConfigWrite }));
    }

    [TestCase("/test/documents-write", HttpStatusCode.OK)]
    [TestCase("/test/documents-read", HttpStatusCode.OK)]
    [TestCase("/test/config-write", HttpStatusCode.Forbidden)]
    [TestCase("/test/platform", HttpStatusCode.Forbidden)]
    public async Task A_seller_passes_document_policies_but_not_config_or_platform(string path, HttpStatusCode expected)
    {
        using var client = _factory.CreateClient(Issue(["seller"], Guid.CreateVersion7()));

        using var response = await client.GetAsync(path);

        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    [TestCase("/test/platform", HttpStatusCode.OK)]
    [TestCase("/test/config-write", HttpStatusCode.Forbidden)]
    public async Task A_platform_admin_without_company_passes_the_platform_policy_only(string path, HttpStatusCode expected)
    {
        using var client = _factory.CreateClient(Issue(["platform_admin"], null));

        using var response = await client.GetAsync(path);

        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    private string Issue(string[] roles, Guid? companyId) =>
        _factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(Guid.CreateVersion7(), roles, companyId).Value;
}
