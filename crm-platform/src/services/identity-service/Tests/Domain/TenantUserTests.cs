using CrmPlatform.IdentityService.Domain.Entities;
using CrmPlatform.IdentityService.Domain.Enums;
using CrmPlatform.IdentityService.Domain.Events;
using FluentAssertions;

namespace CrmPlatform.IdentityService.Tests.Domain;

/// <summary>Unit tests for TenantUser entity.</summary>
public sealed class TenantUserTests
{
    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Create_SetsCorrectProperties()
    {
        var user = TenantUser.Create(TenantA, "obj-123", "alice@contoso.com", "Alice Smith");

        user.TenantId.Should().Be(TenantA);
        user.EntraObjectId.Should().Be("obj-123");
        user.Email.Should().Be("alice@contoso.com");
        user.DisplayName.Should().Be("Alice Smith");
        user.Status.Should().Be(UserStatus.Active);
        user.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Create_PublishesUserProvisionedEvent()
    {
        var user = TenantUser.Create(TenantA, "obj-123", "alice@contoso.com", "Alice Smith");

        user.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<UserProvisionedEvent>();
    }

    [Fact]
    public void Deprovision_SetsStatusAndSoftDeletes()
    {
        var user = TenantUser.Create(TenantA, "obj-123", "alice@contoso.com", "Alice");
        user.ClearDomainEvents();

        user.Deprovision();

        user.Status.Should().Be(UserStatus.Deprovisioned);
        user.IsDeleted.Should().BeTrue();
        user.DomainEvents.Should().Contain(e => e is UserDeprovisionedEvent);
    }

    [Fact]
    public void Deprovision_IsIdempotent()
    {
        var user = TenantUser.Create(TenantA, "obj-123", "alice@contoso.com", "Alice");
        user.Deprovision();
        user.ClearDomainEvents();

        user.Deprovision(); // second call

        user.DomainEvents.Should().BeEmpty("second deprovision should be no-op");
    }

    [Fact]
    public void Suspend_SetsStatusSuspended()
    {
        var user = TenantUser.Create(TenantA, "obj-123", "alice@contoso.com", "Alice");

        user.Suspend();

        user.Status.Should().Be(UserStatus.Suspended);
        user.IsDeleted.Should().BeFalse("suspend is not a soft delete");
    }
}
