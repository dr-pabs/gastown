using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.PlatformAdminService.Domain.Events;

/// <summary>All domain events published by the platform-admin-service to crm.platform topic.</summary>

public record TenantProvisionedEvent(
    Guid TenantId,
    string Slug,
    string PlanId) : DomainEvent(TenantId)
{
    public override string EventType => "tenant.provisioned";
}

public record TenantSuspendedEvent(
    Guid TenantId,
    string SuspendedBy) : DomainEvent(TenantId)
{
    public override string EventType => "tenant.suspended";
}

public record TenantReactivatedEvent(
    Guid TenantId,
    string ReinstatedBy) : DomainEvent(TenantId)
{
    public override string EventType => "tenant.reactivated";
}

public record TenantDeprovisionedEvent(
    Guid TenantId,
    string DeprovisionedBy) : DomainEvent(TenantId)
{
    public override string EventType => "tenant.deprovisioned";
}

public record TenantErasedEvent(
    Guid TenantId) : DomainEvent(TenantId)
{
    public override string EventType => "tenant.erased";
}

public record PlatformHealthDegradedEvent(
    Guid TenantId,
    string ServiceName,
    string Details) : DomainEvent(TenantId)
{
    public override string EventType => "platform.health.degraded";
}
