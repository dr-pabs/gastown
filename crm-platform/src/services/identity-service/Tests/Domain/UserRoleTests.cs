using CrmPlatform.IdentityService.Domain.Entities;
using CrmPlatform.IdentityService.Domain.Enums;
using CrmPlatform.IdentityService.Domain.Events;
using FluentAssertions;

namespace CrmPlatform.IdentityService.Tests.Domain;

public sealed class UserRoleTests
{
    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId  = Guid.NewGuid();

    [Theory]
    [InlineData(TenantRoles.SalesRep)]
    [InlineData(TenantRoles.TenantAdmin)]
    [InlineData(TenantRoles.MarketingUser)]
    public void Grant_ValidRole_Succeeds(string role)
    {
        var userRole = UserRole.Grant(TenantA, UserId, role, "admin@contoso.com");

        userRole.Role.Should().Be(role);
        userRole.TenantUserId.Should().Be(UserId);
        userRole.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<UserRoleGrantedEvent>();
    }

    [Fact]
    public void Grant_PlatformAdmin_Throws()
    {
        var act = () => UserRole.Grant(TenantA, UserId, "PlatformAdmin", "admin@contoso.com");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PlatformAdmin*");
    }

    [Fact]
    public void Grant_UnknownRole_Throws()
    {
        var act = () => UserRole.Grant(TenantA, UserId, "HackerRole", "attacker");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Revoke_SoftDeletesAndPublishesEvent()
    {
        var userRole = UserRole.Grant(TenantA, UserId, TenantRoles.SalesRep, "admin");
        userRole.ClearDomainEvents();

        userRole.Revoke("manager@contoso.com");

        userRole.IsDeleted.Should().BeTrue();
        userRole.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<UserRoleRevokedEvent>();
    }
}
