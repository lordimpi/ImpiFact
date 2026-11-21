using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Identity;
using ImpiFact.Application.Features.Identity.Users;
using ImpiFact.Domain.Identity;

namespace ImpiFact.Application.Tests.Features.Identity;

/// <summary>
/// Rules of a change to a company membership (<see cref="CompanyUserRules"/>): nobody deactivates themselves or removes
/// their own owner role, and the company always keeps at least one active owner.
/// </summary>
public sealed class CompanyUserRulesTests
{
    private static readonly Guid Caller = Guid.CreateVersion7();

    private static CompanyMember Member(string role, bool isActive = true, Guid? id = null) =>
        new(id ?? Guid.CreateVersion7(), "persona@example.com", "Persona", null, role, isActive);

    [Test]
    public void Nobody_deactivates_their_own_membership()
    {
        var self = Member(RoleNames.Seller, id: Caller);

        Assert.That(CompanyUserRules.Check(Caller, self, RoleNames.Seller, isActive: false, activeOwners: 2),
            Is.EqualTo(IdentityErrors.CannotDeactivateSelf));
    }

    [Test]
    public void An_owner_does_not_remove_their_own_owner_role_even_with_other_owners()
    {
        var self = Member(RoleNames.Owner, id: Caller);

        Assert.That(CompanyUserRules.Check(Caller, self, RoleNames.Seller, isActive: true, activeOwners: 3),
            Is.EqualTo(IdentityErrors.CannotRemoveOwnOwnerRole));
    }

    [Test]
    public void Saving_the_same_role_and_state_on_oneself_is_allowed()
    {
        var self = Member(RoleNames.Owner, id: Caller);

        Assert.That(CompanyUserRules.Check(Caller, self, RoleNames.Owner, isActive: true, activeOwners: 1), Is.Null);
    }

    [TestCase(RoleNames.Owner, false, Description = "Deactivate")]
    [TestCase(RoleNames.Seller, true, Description = "Demote")]
    [TestCase(RoleNames.Seller, false, Description = "Both")]
    public void The_last_active_owner_is_never_removed(string role, bool isActive)
    {
        var lastOwner = Member(RoleNames.Owner);

        Assert.That(CompanyUserRules.Check(Caller, lastOwner, role, isActive, activeOwners: 1), Is.EqualTo(IdentityErrors.LastOwner));
    }

    [Test]
    public void An_owner_can_be_removed_when_another_active_owner_remains()
    {
        var owner = Member(RoleNames.Owner);

        Assert.Multiple(() =>
        {
            Assert.That(CompanyUserRules.Check(Caller, owner, RoleNames.Seller, isActive: true, activeOwners: 2), Is.Null);
            Assert.That(CompanyUserRules.Check(Caller, owner, RoleNames.Owner, isActive: false, activeOwners: 2), Is.Null);
        });
    }

    [Test]
    public void An_inactive_owner_or_a_seller_does_not_count_for_the_last_owner_rule()
    {
        var inactiveOwner = Member(RoleNames.Owner, isActive: false);
        var seller = Member(RoleNames.Seller);

        Assert.Multiple(() =>
        {
            Assert.That(CompanyUserRules.Check(Caller, inactiveOwner, RoleNames.Seller, isActive: false, activeOwners: 1), Is.Null);
            Assert.That(CompanyUserRules.Check(Caller, seller, RoleNames.Seller, isActive: false, activeOwners: 1), Is.Null);
            Assert.That(CompanyUserRules.Check(Caller, seller, RoleNames.Owner, isActive: true, activeOwners: 1), Is.Null);
        });
    }

    [Test]
    public void A_credential_without_a_user_has_no_self_rules()
    {
        var owner = Member(RoleNames.Owner, id: Caller);

        Assert.That(CompanyUserRules.Check(actingUserId: null, owner, RoleNames.Owner, isActive: false, activeOwners: 2), Is.Null);
    }
}
