using CrmPlatform.PlatformAdminService.Domain.Enums;
using CrmPlatform.PlatformAdminService.Domain.Events;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.PlatformAdminService.Domain.Entities;

/// <summary>
/// Root aggregate for tenant lifecycle.
/// Only the platform-admin-service may create or mutate Tenant records.
///
/// Note: TenantId == Id for this entity. The Tenant IS the tenant.
/// EF global filter bypassed for all platform-admin operations (PlatformAdmin role required).
/// </summary>
public sealed class Tenant : BaseEntity
{
    private Tenant() { } // EF Core

    public string       Name          { get; private set; } = string.Empty;
    public string       Slug          { get; private set; } = string.Empty;
    public string       PlanId        { get; private set; } = string.Empty;
    public TenantStatus Status        { get; private set; } = TenantStatus.Provisioning;
    public DateTime?    SuspendedAt   { get; private set; }
    public DateTime?    ErasedAt      { get; private set; }
    public string       CreatedBy     { get; private set; } = string.Empty;

    public static Tenant Create(string name, string slug, string planId, string createdBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);

        var tenant = new Tenant
        {
            Name      = name,
            Slug      = slug.ToLowerInvariant(),
            PlanId    = planId,
            Status    = TenantStatus.Provisioning,
            CreatedBy = createdBy,
        };

        // TenantId == Id for the platform Tenant aggregate
        tenant.TenantId = tenant.Id;

        return tenant;
    }

    public void Activate()
    {
        if (Status != TenantStatus.Provisioning)
            throw new InvalidOperationException($"Cannot activate tenant in status {Status}");

        Status = TenantStatus.Active;
        AddDomainEvent(new TenantProvisionedEvent(Id, Slug, PlanId));
    }

    public void Suspend(string suspendedBy)
    {
        if (Status != TenantStatus.Active)
            throw new InvalidOperationException($"Cannot suspend tenant in status {Status}");

        Status      = TenantStatus.Suspended;
        SuspendedAt = DateTime.UtcNow;
        AddDomainEvent(new TenantSuspendedEvent(Id, suspendedBy));
    }

    public void Reinstate(string reinstatedBy)
    {
        if (Status != TenantStatus.Suspended)
            throw new InvalidOperationException($"Cannot reinstate tenant in status {Status}");

        Status      = TenantStatus.Active;
        SuspendedAt = null;
        AddDomainEvent(new TenantReactivatedEvent(Id, reinstatedBy));
    }

    public void BeginDeprovisioning()
    {
        if (Status is not (TenantStatus.Active or TenantStatus.Suspended))
            throw new InvalidOperationException($"Cannot deprovision tenant in status {Status}");

        Status = TenantStatus.Deprovisioning;
    }

    public void CompleteDeprovisioning(string deprovisionedBy)
    {
        if (Status != TenantStatus.Deprovisioning)
            throw new InvalidOperationException($"Cannot complete deprovisioning in status {Status}");

        Status = TenantStatus.Deprovisioned;
        AddDomainEvent(new TenantDeprovisionedEvent(Id, deprovisionedBy));
    }

    public void MarkErased()
    {
        if (Status != TenantStatus.Deprovisioned)
            throw new InvalidOperationException($"Cannot erase tenant in status {Status}");

        Status   = TenantStatus.Erased;
        ErasedAt = DateTime.UtcNow;
        AddDomainEvent(new TenantErasedEvent(Id));
    }

    public void UpdateMetadata(string? name, string? planId)
    {
        if (!string.IsNullOrWhiteSpace(name))   Name   = name;
        if (!string.IsNullOrWhiteSpace(planId)) PlanId = planId;
    }
}
