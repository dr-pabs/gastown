using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.CssService.Domain.Entities;

/// <summary>
/// Immutable audit record of a case escalation.
/// Never soft-deleted — required for compliance audit trail.
/// </summary>
public sealed class EscalationRecord : BaseEntity
{
    public Guid    CaseId             { get; private set; }
    public DateTime EscalatedAt       { get; private set; }
    public Guid    EscalatedBy        { get; private set; }
    public string  Reason             { get; private set; } = string.Empty;
    public Guid?   PreviousAssigneeId { get; private set; }
    public Guid?   NewAssigneeId      { get; private set; }

    private EscalationRecord() { }

    internal static EscalationRecord Create(
        Guid   tenantId,
        Guid   caseId,
        Guid   escalatedBy,
        string reason,
        Guid?  previousAssigneeId,
        Guid?  newAssigneeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new EscalationRecord
        {
            Id                 = Guid.NewGuid(),
            TenantId           = tenantId,
            CaseId             = caseId,
            EscalatedAt        = DateTime.UtcNow,
            EscalatedBy        = escalatedBy,
            Reason             = reason.Trim(),
            PreviousAssigneeId = previousAssigneeId,
            NewAssigneeId      = newAssigneeId,
            CreatedAt          = DateTime.UtcNow,
            UpdatedAt          = DateTime.UtcNow
        };
    }
}
