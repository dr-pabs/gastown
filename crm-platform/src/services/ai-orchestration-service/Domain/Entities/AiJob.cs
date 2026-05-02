using CrmPlatform.AiOrchestrationService.Domain.Enums;

namespace CrmPlatform.AiOrchestrationService.Domain.Entities;

/// <summary>
/// Represents an asynchronous AI work item queued for the AiJobWorker.
/// </summary>
public sealed class AiJob
{
    // ── Private constructor (EF + factory only) ──────────────────────────────
    private AiJob() { }

    // ── Identity & tenant ────────────────────────────────────────────────────
    public Guid Id               { get; private set; }
    public Guid TenantId         { get; private set; }

    // ── Classification ───────────────────────────────────────────────────────
    public CapabilityType CapabilityType { get; private set; }
    public UseCase        UseCase        { get; private set; }

    // ── Who requested this (for failure notifications) ───────────────────────
    public Guid? RequestedByUserId { get; private set; }

    // ── Payload ──────────────────────────────────────────────────────────────
    /// <summary>JSON-serialised input data (lead ID, case ID, prompt vars, etc.).</summary>
    public string InputPayload { get; private set; } = string.Empty;

    // ── Result link ──────────────────────────────────────────────────────────
    public Guid? AiResultId { get; private set; }

    // ── Lifecycle ────────────────────────────────────────────────────────────
    public AiJobStatus Status       { get; private set; }
    public int         AttemptCount { get; private set; }
    public string?     FailureReason { get; private set; }

    // ── Timestamps ───────────────────────────────────────────────────────────
    public DateTime  CreatedAt         { get; private set; }
    public DateTime? FirstAttemptAt    { get; private set; }
    public DateTime? LastAttemptAt     { get; private set; }
    public DateTime? CompletedAt       { get; private set; }
    public DateTime? NextRetryAt       { get; private set; }
    public DateTime? AbandonedAt       { get; private set; }

    // ── Computed ─────────────────────────────────────────────────────────────
    /// <summary>True when the job has not been picked up within the 1-hour TTL.</summary>
    public bool IsStale =>
        Status == AiJobStatus.Queued &&
        CreatedAt.AddHours(1) < DateTime.UtcNow;

    /// <summary>Terminal statuses — no further processing should occur.</summary>
    public bool IsTerminal =>
        Status is AiJobStatus.Succeeded or AiJobStatus.Abandoned;

    // ── Factory ──────────────────────────────────────────────────────────────
    public static AiJob Create(
        Guid           tenantId,
        CapabilityType capabilityType,
        UseCase        useCase,
        Guid?          requestedByUserId,
        string         inputPayload)
    {
        if (string.IsNullOrWhiteSpace(inputPayload))
            throw new ArgumentException("Input payload is required.", nameof(inputPayload));

        return new AiJob
        {
            Id                 = Guid.NewGuid(),
            TenantId           = tenantId,
            CapabilityType     = capabilityType,
            UseCase            = useCase,
            RequestedByUserId  = requestedByUserId,
            InputPayload       = inputPayload,
            Status             = AiJobStatus.Queued,
            AttemptCount       = 0,
            CreatedAt          = DateTime.UtcNow
        };
    }

    // ── Mutators ─────────────────────────────────────────────────────────────
    public void MarkInProgress()
    {
        if (Status != AiJobStatus.Queued && Status != AiJobStatus.Failed)
            throw new InvalidOperationException($"Cannot start a job in status '{Status}'.");

        Status        = AiJobStatus.InProgress;
        AttemptCount += 1;
        LastAttemptAt = DateTime.UtcNow;
        FirstAttemptAt ??= LastAttemptAt;
        NextRetryAt   = null;
    }

    public void MarkSucceeded(Guid? aiResultId = null)
    {
        if (Status != AiJobStatus.InProgress)
            throw new InvalidOperationException($"Cannot succeed a job in status '{Status}'.");

        Status        = AiJobStatus.Succeeded;
        AiResultId    = aiResultId;
        CompletedAt   = DateTime.UtcNow;
        FailureReason = null;
        NextRetryAt   = null;
    }

    public void MarkFailed(string reason, DateTime? nextRetryAt = null)
    {
        if (Status != AiJobStatus.InProgress)
            throw new InvalidOperationException($"Cannot fail a job in status '{Status}'.");

        Status        = AiJobStatus.Failed;
        FailureReason = reason;
        NextRetryAt   = nextRetryAt;
    }

    public void Abandon(string reason)
    {
        if (IsTerminal)
            throw new InvalidOperationException($"Cannot abandon a terminal job (status '{Status}').");

        Status        = AiJobStatus.Abandoned;
        FailureReason = reason;
        AbandonedAt   = DateTime.UtcNow;
        NextRetryAt   = null;
    }
}
