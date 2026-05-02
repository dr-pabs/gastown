using CrmPlatform.MarketingService.Domain.Enums;
using CrmPlatform.MarketingService.Domain.Events;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.MarketingService.Domain.Entities;

/// <summary>
/// Tracks a single contact's progress through a Journey.
/// The Durable Function orchestrator updates this record as steps are processed.
/// </summary>
public sealed class JourneyEnrollment : BaseEntity
{
    public Guid             JourneyId             { get; private set; }
    public Guid             ContactId             { get; private set; }
    public EnrollmentStatus Status                { get; private set; }
    public int              CurrentStepIndex      { get; private set; }
    public DateTime?        CompletedAt           { get; private set; }
    public DateTime?        ExitedAt              { get; private set; }
    public string?          ExitReason            { get; private set; }

    /// <summary>Durable Function InstanceId — used to raise external events (e.g. cancel).</summary>
    public string? DurableFunctionInstanceId      { get; private set; }

    // Navigation
    public Journey? Journey { get; private set; }

    private JourneyEnrollment() { } // EF Core

    public static JourneyEnrollment Create(
        Guid tenantId,
        Guid journeyId,
        Guid contactId)
    {
        return new JourneyEnrollment
        {
            Id               = Guid.NewGuid(),
            TenantId         = tenantId,
            JourneyId        = journeyId,
            ContactId        = contactId,
            Status           = EnrollmentStatus.Active,
            CurrentStepIndex = 0,
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow
        };
    }

    /// <summary>Store the Durable Function instance ID immediately after orchestration starts.</summary>
    public void SetDurableFunctionInstanceId(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        DurableFunctionInstanceId = instanceId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Advance to the next step (called by Durable Function activity).</summary>
    public void AdvanceStep()
    {
        if (Status != EnrollmentStatus.Active)
            throw new InvalidOperationException($"Cannot advance enrollment in status {Status}.");

        CurrentStepIndex++;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Mark the enrollment as successfully completed.</summary>
    public void Complete()
    {
        if (Status != EnrollmentStatus.Active)
            throw new InvalidOperationException($"Cannot complete enrollment in status {Status}.");

        Status      = EnrollmentStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt   = DateTime.UtcNow;

        // Publish the event that sfa-service JourneyCompletedConsumer listens to
        AddDomainEvent(new JourneyCompletedEvent(
            Id, TenantId, JourneyId, ContactId, DateTime.UtcNow));
    }

    /// <summary>Exit the enrollment (opt-out or disqualification).</summary>
    public void Exit(string reason)
    {
        if (Status != EnrollmentStatus.Active)
            throw new InvalidOperationException($"Cannot exit enrollment in status {Status}.");

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Status     = EnrollmentStatus.Exited;
        ExitedAt   = DateTime.UtcNow;
        ExitReason = reason;
        UpdatedAt  = DateTime.UtcNow;
    }

    /// <summary>Mark as failed due to an unrecoverable processing error.</summary>
    public void Fail(string reason)
    {
        Status     = EnrollmentStatus.Failed;
        ExitReason = reason;
        UpdatedAt  = DateTime.UtcNow;
    }
}
