namespace CrmPlatform.PlatformAdminService.Domain.Enums;

/// <summary>
/// Tenant lifecycle states.
/// Allowed transitions:
///   Provisioning → Active
///   Active → Suspended
///   Suspended → Active (reinstate)
///   Active | Suspended → Deprovisioning
///   Deprovisioning → Deprovisioned
///   Deprovisioned → Erased (GDPR)
/// </summary>
public enum TenantStatus
{
    Provisioning,
    Active,
    Suspended,
    Deprovisioning,
    Deprovisioned,
    Erased,
}

public enum ProvisioningStepStatus
{
    Pending,
    Completed,
    Failed,
    Compensating,
    Compensated,
}
