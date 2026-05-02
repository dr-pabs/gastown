using CrmPlatform.IdentityService.Domain.Enums;
using CrmPlatform.IdentityService.Domain.Events;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.IdentityService.Domain.Entities;

/// <summary>
/// Represents a single role assignment for a TenantUser within a tenant.
/// A user may hold multiple roles simultaneously.
///
/// Rule: PlatformAdmin must never be stored here — validate in GrantRoleCommand.
/// </summary>
public sealed class UserRole : BaseEntity
{
    private UserRole() { } // EF Core

    public Guid   TenantUserId { get; private set; }
    public string Role         { get; private set; } = string.Empty;
    public DateTime GrantedAt  { get; private set; }
    public string GrantedBy    { get; private set; } = string.Empty;

    // Navigation
    public TenantUser? TenantUser { get; private set; }

    public static UserRole Grant(
        Guid tenantId,
        Guid tenantUserId,
        string role,
        string grantedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(grantedBy);

        if (!TenantRoles.IsValidTenantRole(role))
            throw new InvalidOperationException(
                $"Role '{role}' is not a valid tenant role. PlatformAdmin may not be stored as a UserRole.");

        var userRole = new UserRole
        {
            TenantId     = tenantId,
            TenantUserId = tenantUserId,
            Role         = role,
            GrantedAt    = DateTime.UtcNow,
            GrantedBy    = grantedBy,
        };

        userRole.AddDomainEvent(new UserRoleGrantedEvent(tenantUserId, tenantId, role, grantedBy));
        return userRole;
    }

    public void Revoke(string revokedBy)
    {
        SoftDelete();
        AddDomainEvent(new UserRoleRevokedEvent(TenantUserId, TenantId, Role, revokedBy));
    }
}
