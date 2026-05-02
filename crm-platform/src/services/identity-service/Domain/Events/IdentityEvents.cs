using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.IdentityService.Domain.Events;

/// <summary>
/// All domain events published by the identity-service to crm.identity topic.
/// Each event also shadows to crm.analytics from day one.
/// </summary>

public record UserProvisionedEvent(
    Guid UserId,
    Guid TenantId,
    string Email,
    string DisplayName) : DomainEvent(TenantId)
{
    public override string EventType => "user.provisioned";
}

public record UserDeprovisionedEvent(
    Guid UserId,
    Guid TenantId) : DomainEvent(TenantId)
{
    public override string EventType => "user.deprovisioned";
}

public record UserRoleGrantedEvent(
    Guid UserId,
    Guid TenantId,
    string Role,
    string GrantedBy) : DomainEvent(TenantId)
{
    public override string EventType => "user.role.granted";
}

public record UserRoleRevokedEvent(
    Guid UserId,
    Guid TenantId,
    string Role,
    string RevokedBy) : DomainEvent(TenantId)
{
    public override string EventType => "user.role.revoked";
}

public record ConsentRecordedEvent(
    Guid UserId,
    Guid TenantId,
    string ConsentType) : DomainEvent(TenantId)
{
    public override string EventType => "consent.recorded";
}
