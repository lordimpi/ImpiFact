using ImpiFact.Application.Abstractions.Identity;

namespace ImpiFact.Application.Tests.Features.Identity;

/// <summary>
/// Alcances (scopes) que recibe un usuario del panel según sus roles, al emitir su token (Arquitectura detallada,
/// sección 9.2). Los de empresa solo con empresa activa; <c>platform:*</c> solo sin empresa.
/// </summary>
[TestFixture]
public sealed class RoleScopesTests
{
    private static readonly Guid Company = Guid.CreateVersion7();

    [Test]
    public void An_owner_gets_documents_and_config_scopes_in_the_company() =>
        Assert.That(RoleScopes.For(["owner"], Company),
            Is.EqualTo(new[] { ApiScopes.DocumentsRead, ApiScopes.DocumentsWrite, ApiScopes.ConfigWrite }));

    [Test]
    public void A_seller_gets_documents_scopes_only() =>
        Assert.That(RoleScopes.For(["seller"], Company), Is.EqualTo(new[] { ApiScopes.DocumentsRead, ApiScopes.DocumentsWrite }));

    [Test]
    public void A_customer_gets_documents_read_only() =>
        Assert.That(RoleScopes.For(["customer"], Company), Is.EqualTo(new[] { ApiScopes.DocumentsRead }));

    [Test]
    public void A_platform_admin_without_company_gets_the_platform_scope() =>
        Assert.That(RoleScopes.For(["platform_admin"], null), Is.EqualTo(new[] { ApiScopes.Platform }));

    [Test]
    public void A_platform_admin_with_an_active_company_does_not_get_the_platform_scope() =>
        Assert.That(RoleScopes.For(["platform_admin"], Company), Is.Empty);

    [Test]
    public void Company_roles_give_nothing_without_an_active_company() =>
        Assert.That(RoleScopes.For(["owner", "seller", "customer"], null), Is.Empty);

    [TestCase("reseller")]
    [TestCase("unknown-role")]
    public void Other_roles_give_no_scopes(string role)
    {
        Assert.Multiple(() =>
        {
            Assert.That(RoleScopes.For([role], Company), Is.Empty);
            Assert.That(RoleScopes.For([role], null), Is.Empty);
        });
    }

    [Test]
    public void Several_roles_give_the_union_without_repetitions() =>
        Assert.That(RoleScopes.For(["seller", "owner", "customer"], Company),
            Is.EqualTo(new[] { ApiScopes.DocumentsRead, ApiScopes.DocumentsWrite, ApiScopes.ConfigWrite }));
}
