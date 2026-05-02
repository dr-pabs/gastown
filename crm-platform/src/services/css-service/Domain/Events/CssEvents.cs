using CrmPlatform.CssService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.CssService.Domain.Events;

// ─── Case events ──────────────────────────────────────────────────────────────

public sealed record CaseCreatedEvent(
    Guid         CaseId,
    Guid         TenantId,
    Guid         CreatedByUserId,
    CasePriority Priority)
    : DomainEvent(TenantId)
{
    public override string EventType => "case.created";
}

public sealed record CaseAssignedEvent(
    Guid CaseId,
    Guid TenantId,
    Guid AssignedToUserId,
    Guid AssignedByUserId)
    : DomainEvent(TenantId)
{
    public override string EventType => "case.assigned";
}

public sealed record CaseStatusChangedEvent(
    Guid       CaseId,
    Guid       TenantId,
    CaseStatus PreviousStatus,
    CaseStatus NewStatus)
    : DomainEvent(TenantId)
{
    public override string EventType => "case.status.changed";
}

public sealed record CaseEscalatedEvent(
    Guid   CaseId,
    Guid   TenantId,
    Guid   EscalatedBy,
    string Reason)
    : DomainEvent(TenantId)
{
    public override string EventType => "case.escalated";
}

public sealed record CaseResolvedEvent(
    Guid CaseId,
    Guid TenantId)
    : DomainEvent(TenantId)
{
    public override string EventType => "case.resolved";
}

public sealed record CaseClosedEvent(
    Guid CaseId,
    Guid TenantId)
    : DomainEvent(TenantId)
{
    public override string EventType => "case.closed";
}

public sealed record SlaBreachedEvent(
    Guid     CaseId,
    Guid     TenantId,
    string   Severity,     // "Warning" (80%) | "Breached" (100%)
    DateTime SlaDeadline,
    DateTime DetectedAt)
    : DomainEvent(TenantId)
{
    public override string EventType => "sla.breached";
}
