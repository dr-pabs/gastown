using CrmPlatform.CssService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.CssService.Domain.Entities;

/// <summary>
/// Per-tenant SLA configuration keyed by CasePriority.
/// </summary>
public sealed class SlaPolicy : BaseEntity
{
    public string       Name                   { get; private set; } = string.Empty;
    public CasePriority Priority               { get; private set; }
    public int          FirstResponseMinutes   { get; private set; }
    public int          ResolutionMinutes      { get; private set; }
    public bool         BusinessHoursOnly      { get; private set; }

    private SlaPolicy() { }

    public static SlaPolicy Create(
        Guid        tenantId,
        string      name,
        CasePriority priority,
        int         firstResponseMinutes,
        int         resolutionMinutes,
        bool        businessHoursOnly)
    {
        if (firstResponseMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(firstResponseMinutes), "Must be positive.");
        if (resolutionMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(resolutionMinutes), "Must be positive.");

        return new SlaPolicy
        {
            Id                   = Guid.NewGuid(),
            TenantId             = tenantId,
            Name                 = name.Trim(),
            Priority             = priority,
            FirstResponseMinutes = firstResponseMinutes,
            ResolutionMinutes    = resolutionMinutes,
            BusinessHoursOnly    = businessHoursOnly,
            CreatedAt            = DateTime.UtcNow,
            UpdatedAt            = DateTime.UtcNow
        };
    }

    /// <summary>Compute the SLA deadline for a case opened now.</summary>
    public DateTime CalculateDeadline(DateTime openedAt) =>
        openedAt.AddMinutes(ResolutionMinutes);

    /// <summary>80% early-warning threshold for SLA breach alert.</summary>
    public DateTime CalculateWarningThreshold(DateTime openedAt) =>
        openedAt.AddMinutes(ResolutionMinutes * 0.8);
}
