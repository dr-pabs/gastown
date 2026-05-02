using CrmPlatform.IdentityService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.IdentityService.Domain.Entities;

/// <summary>
/// Immutable audit record for every provisioning action on a TenantUser.
/// Rules:
///   - Never soft-deleted, never hard-deleted, never updated.
///   - Written by command handlers only — never by consumers directly.
/// </summary>
public sealed class UserProvisioningLog : BaseEntity
{
    private UserProvisioningLog() { } // EF Core

    public Guid                   TenantUserId  { get; private set; }
    public UserProvisioningAction Action        { get; private set; }
    public DateTime               OccurredAt    { get; private set; }
    public string                 InitiatedBy   { get; private set; } = string.Empty;
    public string?                Details       { get; private set; }

    // Navigation
    public TenantUser? TenantUser { get; private set; }

    public static UserProvisioningLog Write(
        Guid tenantId,
        Guid tenantUserId,
        UserProvisioningAction action,
        string initiatedBy,
        string? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(initiatedBy);

        return new UserProvisioningLog
        {
            TenantId     = tenantId,
            TenantUserId = tenantUserId,
            Action       = action,
            OccurredAt   = DateTime.UtcNow,
            InitiatedBy  = initiatedBy,
            Details      = details,
        };
    }
}
