using CrmPlatform.PlatformAdminService.Domain.Entities;
using CrmPlatform.PlatformAdminService.Domain.Enums;
using CrmPlatform.PlatformAdminService.Domain.Events;
using FluentAssertions;

namespace CrmPlatform.PlatformAdminService.Tests.Domain;

public sealed class TenantTests
{
    // ── Factory ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsFieldsAndStatusIsProvisioning()
    {
        var tenant = Tenant.Create("Acme Corp", "acme", "starter", "admin@system");

        tenant.Name.Should().Be("Acme Corp");
        tenant.Slug.Should().Be("acme");
        tenant.PlanId.Should().Be("starter");
        tenant.Status.Should().Be(TenantStatus.Provisioning);
        tenant.TenantId.Should().Be(tenant.Id);
        tenant.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Create_SlugIsLowerCased()
    {
        var tenant = Tenant.Create("Acme Corp", "ACME-CORP", "pro", "admin");
        tenant.Slug.Should().Be("acme-corp");
    }

    // ── Activate ─────────────────────────────────────────────────────────────

    [Fact]
    public void Activate_FromProvisioning_SetsActiveAndRaisesTenantProvisionedEvent()
    {
        var tenant = Tenant.Create("Beta", "beta", "pro", "admin");
        tenant.Activate();

        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.DomainEvents.Should().ContainSingle(e => e is TenantProvisionedEvent);
    }

    [Fact]
    public void Activate_WhenAlreadyActive_Throws()
    {
        var tenant = Tenant.Create("Beta", "beta", "pro", "admin");
        tenant.Activate();

        var act = () => tenant.Activate();
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Suspend ───────────────────────────────────────────────────────────────

    [Fact]
    public void Suspend_FromActive_SetsSuspendedAndRaisesEvent()
    {
        var tenant = BuildActiveTenant();
        tenant.Suspend("ops-team");

        tenant.Status.Should().Be(TenantStatus.Suspended);
        tenant.SuspendedAt.Should().NotBeNull();
        tenant.DomainEvents.Should().Contain(e => e is TenantSuspendedEvent);
    }

    [Fact]
    public void Suspend_WhenNotActive_Throws()
    {
        var tenant = BuildActiveTenant();
        tenant.Suspend("ops");
        // Already suspended — second call should throw
        var act = () => tenant.Suspend("ops");
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Reinstate ─────────────────────────────────────────────────────────────

    [Fact]
    public void Reinstate_FromSuspended_SetsActiveAndClearsSuspendedAt()
    {
        var tenant = BuildActiveTenant();
        tenant.Suspend("ops");
        tenant.DomainEvents.Clear();
        tenant.Reinstate("ops");

        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.SuspendedAt.Should().BeNull();
        tenant.DomainEvents.Should().Contain(e => e is TenantReactivatedEvent);
    }

    [Fact]
    public void Reinstate_WhenNotSuspended_Throws()
    {
        var tenant = BuildActiveTenant();
        var act = () => tenant.Reinstate("ops");
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Deprovision ───────────────────────────────────────────────────────────

    [Fact]
    public void Deprovision_FullLifecycle_ReachesDeprovisionedStatus()
    {
        var tenant = BuildActiveTenant();
        tenant.BeginDeprovisioning();
        tenant.Status.Should().Be(TenantStatus.Deprovisioning);

        tenant.CompleteDeprovisioning("admin");
        tenant.Status.Should().Be(TenantStatus.Deprovisioned);
        tenant.DomainEvents.Should().Contain(e => e is TenantDeprovisionedEvent);
    }

    [Fact]
    public void BeginDeprovisioning_WhenProvisioning_Throws()
    {
        var tenant = Tenant.Create("X", "x", "free", "admin");
        var act = () => tenant.BeginDeprovisioning();
        act.Should().Throw<InvalidOperationException>();
    }

    // ── GDPR Erase ────────────────────────────────────────────────────────────

    [Fact]
    public void MarkErased_FromDeprovisioned_SetsErasedStatus()
    {
        var tenant = BuildDeprovisionedTenant();
        tenant.MarkErased();

        tenant.Status.Should().Be(TenantStatus.Erased);
        tenant.ErasedAt.Should().NotBeNull();
        tenant.DomainEvents.Should().Contain(e => e is TenantErasedEvent);
    }

    [Fact]
    public void MarkErased_WhenNotDeprovisioned_Throws()
    {
        var tenant = BuildActiveTenant();
        var act = () => tenant.MarkErased();
        act.Should().Throw<InvalidOperationException>();
    }

    // ── UpdateMetadata ────────────────────────────────────────────────────────

    [Fact]
    public void UpdateMetadata_ChangesNameAndPlan()
    {
        var tenant = BuildActiveTenant();
        tenant.UpdateMetadata("New Name", "enterprise");

        tenant.Name.Should().Be("New Name");
        tenant.PlanId.Should().Be("enterprise");
    }

    [Fact]
    public void UpdateMetadata_NullValues_DoesNotOverwrite()
    {
        var tenant = BuildActiveTenant();
        var originalName = tenant.Name;
        tenant.UpdateMetadata(null, null);

        tenant.Name.Should().Be(originalName);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Tenant BuildActiveTenant()
    {
        var t = Tenant.Create("Acme", "acme", "pro", "admin");
        t.Activate();
        t.ClearDomainEvents();
        return t;
    }

    private static Tenant BuildDeprovisionedTenant()
    {
        var t = BuildActiveTenant();
        t.BeginDeprovisioning();
        t.CompleteDeprovisioning("admin");
        t.ClearDomainEvents();
        return t;
    }
}
