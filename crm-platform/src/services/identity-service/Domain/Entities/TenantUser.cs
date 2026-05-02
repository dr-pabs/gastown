using CrmPlatform.IdentityService.Domain.Enums;
using CrmPlatform.IdentityService.Domain.Events;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.IdentityService.Domain.Entities;

/// <summary>
/// Represents a user provisioned into a specific tenant.
/// Owns the canonical identity record within the tenant boundary.
/// </summary>
public sealed class TenantUser : BaseEntity
{
    private TenantUser() { } // EF Core

    public string EntraObjectId { get; private set; } = string.Empty;
    public string Email          { get; private set; } = string.Empty;
    public string DisplayName    { get; private set; } = string.Empty;
    public UserStatus Status      { get; private set; } = UserStatus.Active;

    // Navigation
    public ICollection<UserRole>            Roles             { get; private set; } = [];
    public ICollection<ConsentRecord>       ConsentRecords    { get; private set; } = [];
    public ICollection<UserProvisioningLog> ProvisioningLogs  { get; private set; } = [];

    public static TenantUser Create(
        Guid tenantId,
        string entraObjectId,
        string email,
        string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entraObjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var user = new TenantUser
        {
            TenantId       = tenantId,
            EntraObjectId  = entraObjectId,
            Email          = email,
            DisplayName    = displayName,
            Status         = UserStatus.Active,
        };

        user.AddDomainEvent(new UserProvisionedEvent(user.Id, tenantId, email, displayName));
        return user;
    }

    public void Deprovision()
    {
        if (Status == UserStatus.Deprovisioned) return;

        Status = UserStatus.Deprovisioned;
        SoftDelete();
        AddDomainEvent(new UserDeprovisionedEvent(Id, TenantId));
    }

    public void Suspend()
    {
        if (Status == UserStatus.Suspended) return;
        Status = UserStatus.Suspended;
    }

    public void Reinstate()
    {
        if (Status != UserStatus.Suspended) return;
        Status = UserStatus.Active;
    }
}
