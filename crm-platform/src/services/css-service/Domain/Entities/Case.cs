using CrmPlatform.CssService.Domain.Enums;
using CrmPlatform.CssService.Domain.Events;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.CssService.Domain.Entities;

public sealed class Case : BaseEntity
{
    public string      Title                     { get; private set; } = string.Empty;
    public string      Description               { get; private set; } = string.Empty;
    public CaseStatus  Status                    { get; private set; }
    public CasePriority Priority                 { get; private set; }
    public CaseChannel  Channel                  { get; private set; }
    public Guid?        ContactId                { get; private set; }
    public Guid?        AccountId                { get; private set; }
    public Guid?        AssignedToUserId         { get; private set; }
    public DateTime?    SlaDeadline              { get; private set; }
    public string?      DurableFunctionInstanceId { get; private set; }
    public bool         SlaBreached              { get; private set; }

    // Navigation
    public IReadOnlyList<CaseComment>   Comments    { get; private set; } = [];
    public IReadOnlyList<EscalationRecord> Escalations { get; private set; } = [];

    private Case() { } // EF Core

    public static Case Create(
        Guid         tenantId,
        string       title,
        string       description,
        CasePriority priority,
        CaseChannel  channel,
        Guid?        contactId,
        Guid?        accountId,
        Guid         createdByUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var c = new Case
        {
            Id          = Guid.NewGuid(),
            TenantId    = tenantId,
            Title       = title.Trim(),
            Description = description.Trim(),
            Status      = CaseStatus.New,
            Priority    = priority,
            Channel     = channel,
            ContactId   = contactId,
            AccountId   = accountId,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow
        };

        c.AddDomainEvent(new CaseCreatedEvent(c.Id, tenantId, createdByUserId, priority));
        return c;
    }

    /// <summary>Transitions the case from New → Open. Starts the SLA clock.</summary>
    public void Open(DateTime slaDeadline)
    {
        GuardNotClosed();
        if (Status != CaseStatus.New)
            throw new InvalidOperationException($"Cannot open a case in status {Status}.");

        Status      = CaseStatus.Open;
        SlaDeadline = slaDeadline;
        UpdatedAt   = DateTime.UtcNow;

        AddDomainEvent(new CaseStatusChangedEvent(Id, TenantId, CaseStatus.New, CaseStatus.Open));
    }

    /// <summary>Transitions Open/Escalated → Pending (waiting on customer reply).</summary>
    public void SetPending()
    {
        GuardNotClosed();
        if (Status is not (CaseStatus.Open or CaseStatus.Escalated))
            throw new InvalidOperationException($"Cannot set Pending from status {Status}.");

        var prev  = Status;
        Status    = CaseStatus.Pending;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new CaseStatusChangedEvent(Id, TenantId, prev, CaseStatus.Pending));
    }

    /// <summary>Resumes SLA clock: Pending → Open on customer reply.</summary>
    public void Resume()
    {
        GuardNotClosed();
        if (Status != CaseStatus.Pending)
            throw new InvalidOperationException($"Cannot resume from status {Status}.");

        Status    = CaseStatus.Open;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new CaseStatusChangedEvent(Id, TenantId, CaseStatus.Pending, CaseStatus.Open));
    }

    /// <summary>Escalate — creates an EscalationRecord, changes status to Escalated.</summary>
    public EscalationRecord Escalate(
        Guid   escalatedBy,
        string reason,
        Guid?  newAssigneeId)
    {
        GuardNotClosed();
        if (Status is CaseStatus.Resolved or CaseStatus.Closed)
            throw new InvalidOperationException("Cannot escalate a resolved or closed case.");

        var prev  = Status;
        Status    = CaseStatus.Escalated;
        AssignedToUserId = newAssigneeId ?? AssignedToUserId;
        UpdatedAt = DateTime.UtcNow;

        var record = EscalationRecord.Create(
            TenantId, Id, escalatedBy, reason, AssignedToUserId, newAssigneeId);

        AddDomainEvent(new CaseEscalatedEvent(Id, TenantId, escalatedBy, reason));
        AddDomainEvent(new CaseStatusChangedEvent(Id, TenantId, prev, CaseStatus.Escalated));
        return record;
    }

    /// <summary>Complete escalation: Escalated → Open with new assignee.</summary>
    public void CompleteEscalation(Guid? newAssigneeId)
    {
        if (Status != CaseStatus.Escalated)
            throw new InvalidOperationException("Case is not in Escalated state.");

        Status           = CaseStatus.Open;
        AssignedToUserId = newAssigneeId ?? AssignedToUserId;
        UpdatedAt        = DateTime.UtcNow;

        AddDomainEvent(new CaseStatusChangedEvent(Id, TenantId, CaseStatus.Escalated, CaseStatus.Open));
    }

    /// <summary>Resolve: Open/Pending/Escalated → Resolved.</summary>
    public void Resolve()
    {
        GuardNotClosed();
        if (Status is CaseStatus.Resolved)
            throw new InvalidOperationException("Case is already resolved.");

        var prev  = Status;
        Status    = CaseStatus.Resolved;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new CaseResolvedEvent(Id, TenantId));
        AddDomainEvent(new CaseStatusChangedEvent(Id, TenantId, prev, CaseStatus.Resolved));
    }

    /// <summary>Close: Resolved → Closed. Closed cases are immutable.</summary>
    public void Close()
    {
        if (Status != CaseStatus.Resolved)
            throw new InvalidOperationException("Only Resolved cases can be Closed.");

        Status    = CaseStatus.Closed;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new CaseClosedEvent(Id, TenantId));
        AddDomainEvent(new CaseStatusChangedEvent(Id, TenantId, CaseStatus.Resolved, CaseStatus.Closed));
    }

    public void Assign(Guid assignedToUserId, Guid assignedByUserId)
    {
        GuardNotClosed();
        AssignedToUserId = assignedToUserId;
        UpdatedAt        = DateTime.UtcNow;

        AddDomainEvent(new CaseAssignedEvent(Id, TenantId, assignedToUserId, assignedByUserId));
    }

    public void MarkSlaBreached()
    {
        SlaBreached = true;
        UpdatedAt   = DateTime.UtcNow;
    }

    public void SetDurableFunctionInstanceId(string instanceId)
    {
        DurableFunctionInstanceId = instanceId;
        UpdatedAt = DateTime.UtcNow;
    }

    private void GuardNotClosed()
    {
        if (Status == CaseStatus.Closed)
            throw new InvalidOperationException("Case is closed and immutable.");
    }
}
