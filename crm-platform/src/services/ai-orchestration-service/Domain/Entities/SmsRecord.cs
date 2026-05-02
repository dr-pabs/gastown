namespace CrmPlatform.AiOrchestrationService.Domain.Entities;

/// <summary>
/// Audit record for every AI-composed SMS sent from this service via ACS.
/// </summary>
public sealed class SmsRecord
{
    private SmsRecord() { }

    public Guid   Id               { get; private set; }
    public Guid   TenantId         { get; private set; }

    // ── Link to the job that composed this SMS (optional for sync paths) ─────
    public Guid?  JobId            { get; private set; }

    // ── Message ──────────────────────────────────────────────────────────────
    public string RecipientPhone   { get; private set; } = string.Empty;
    public string ComposedMessage  { get; private set; } = string.Empty;

    // ── Delivery state ───────────────────────────────────────────────────────
    public bool    IsSent          { get; private set; }
    public bool    IsFailed        { get; private set; }
    public string? AcsMessageId    { get; private set; }
    public string? FailureReason   { get; private set; }

    // ── Timestamps ───────────────────────────────────────────────────────────
    public DateTime  CreatedAt     { get; private set; }
    public DateTime? SentAt        { get; private set; }
    public DateTime? FailedAt      { get; private set; }

    // ── Factory ──────────────────────────────────────────────────────────────
    public static SmsRecord Create(
        Guid    tenantId,
        string  recipientPhone,
        string  composedMessage,
        Guid?   jobId = null)
    {
        if (string.IsNullOrWhiteSpace(recipientPhone))
            throw new ArgumentException("Recipient phone is required.", nameof(recipientPhone));
        if (string.IsNullOrWhiteSpace(composedMessage))
            throw new ArgumentException("Composed message is required.", nameof(composedMessage));

        return new SmsRecord
        {
            Id              = Guid.NewGuid(),
            TenantId        = tenantId,
            JobId           = jobId,
            RecipientPhone  = recipientPhone,
            ComposedMessage = composedMessage,
            CreatedAt       = DateTime.UtcNow
        };
    }

    // ── Mutators ─────────────────────────────────────────────────────────────
    public void MarkSent(string acsMessageId)
    {
        if (IsSent || IsFailed)
            throw new InvalidOperationException("SMS record is already in a terminal state.");

        IsSent       = true;
        AcsMessageId = acsMessageId;
        SentAt       = DateTime.UtcNow;
    }

    public void MarkFailed(string reason)
    {
        if (IsSent || IsFailed)
            throw new InvalidOperationException("SMS record is already in a terminal state.");

        IsFailed      = true;
        FailureReason = reason;
        FailedAt      = DateTime.UtcNow;
    }
}
