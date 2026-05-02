using CrmPlatform.PlatformAdminService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.PlatformAdminService.Domain.Entities;

/// <summary>
/// Immutable audit record for every provisioning saga step.
/// Never deleted, never updated.
/// </summary>
public sealed class TenantProvisioningLog : BaseEntity
{
    private TenantProvisioningLog() { } // EF Core

    public Guid                   TenantEntityId { get; private set; }
    public string                 Step           { get; private set; } = string.Empty;
    public ProvisioningStepStatus StepStatus     { get; private set; }
    public DateTime               OccurredAt     { get; private set; }
    public string?                Details        { get; private set; }

    public Tenant? Tenant { get; private set; }

    public static TenantProvisioningLog Write(
        Guid tenantId,
        Guid tenantEntityId,
        string step,
        ProvisioningStepStatus stepStatus,
        string? details = null)
    {
        return new TenantProvisioningLog
        {
            TenantId       = tenantId,
            TenantEntityId = tenantEntityId,
            Step           = step,
            StepStatus     = stepStatus,
            OccurredAt     = DateTime.UtcNow,
            Details        = details,
        };
    }
}
