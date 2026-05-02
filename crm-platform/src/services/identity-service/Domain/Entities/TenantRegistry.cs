using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.IdentityService.Domain.Entities;

public enum TenantRegistryStatus
{
    Active,
    Suspended,
    Deprovisioned,
}

/// <summary>
/// Maps the platform TenantId to the Entra and External ID tenant identifiers.
/// Used exclusively by the APIM/middleware tenant-lookup endpoint.
/// Not exposed to external callers.
/// </summary>
public sealed class TenantRegistry : BaseEntity
{
    private TenantRegistry() { } // EF Core

    public Guid   EntraTenantId      { get; private set; }
    public Guid   ExternalIdTenantId { get; private set; }
    public TenantRegistryStatus Status { get; private set; } = TenantRegistryStatus.Active;

    public static TenantRegistry Create(
        Guid tenantId,
        Guid entraTenantId,
        Guid externalIdTenantId)
    {
        return new TenantRegistry
        {
            TenantId          = tenantId,
            EntraTenantId      = entraTenantId,
            ExternalIdTenantId = externalIdTenantId,
            Status             = TenantRegistryStatus.Active,
        };
    }

    public void Suspend()    => Status = TenantRegistryStatus.Suspended;
    public void Reinstate()  => Status = TenantRegistryStatus.Active;
    public void Deprovision() => Status = TenantRegistryStatus.Deprovisioned;
}
