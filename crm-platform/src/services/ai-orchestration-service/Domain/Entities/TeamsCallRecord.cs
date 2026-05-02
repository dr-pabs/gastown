namespace CrmPlatform.AiOrchestrationService.Domain.Entities;

/// <summary>
/// Audit record for every Teams outbound call initiated by this service via ACS Calling.
/// Transcript is written back asynchronously once the call ends.
/// </summary>
public sealed class TeamsCallRecord
{
    private TeamsCallRecord() { }

    public Guid   Id                { get; private set; }
    public Guid   TenantId          { get; private set; }

    // ── Participants ─────────────────────────────────────────────────────────
    /// <summary>ACS user ID of the staff member being called.</summary>
    public string TargetUserId      { get; private set; } = string.Empty;

    // ── Context (JSON blob describing why the call was initiated) ────────────
    public string CallContext       { get; private set; } = string.Empty;

    // ── ACS call reference ───────────────────────────────────────────────────
    public string? AcsCallId        { get; private set; }

    // ── State ────────────────────────────────────────────────────────────────
    public bool    IsConnected      { get; private set; }
    public bool    IsEnded          { get; private set; }
    public int?    DurationSeconds  { get; private set; }

    // ── Transcript ───────────────────────────────────────────────────────────
    public string? TranscriptText   { get; private set; }
    public DateTime? TranscriptAt  { get; private set; }

    // ── Timestamps ───────────────────────────────────────────────────────────
    public DateTime  CreatedAt      { get; private set; }
    public DateTime? ConnectedAt    { get; private set; }
    public DateTime? EndedAt        { get; private set; }

    // ── Factory ──────────────────────────────────────────────────────────────
    public static TeamsCallRecord Create(
        Guid   tenantId,
        string targetUserId,
        string callContext)
    {
        if (string.IsNullOrWhiteSpace(targetUserId))
            throw new ArgumentException("Target user ID is required.", nameof(targetUserId));
        if (string.IsNullOrWhiteSpace(callContext))
            throw new ArgumentException("Call context is required.", nameof(callContext));

        return new TeamsCallRecord
        {
            Id           = Guid.NewGuid(),
            TenantId     = tenantId,
            TargetUserId = targetUserId,
            CallContext  = callContext,
            CreatedAt    = DateTime.UtcNow
        };
    }

    // ── Mutators ─────────────────────────────────────────────────────────────
    public void MarkConnected(string acsCallId)
    {
        if (IsEnded)
            throw new InvalidOperationException("Cannot connect a call that has already ended.");

        AcsCallId   = acsCallId;
        IsConnected = true;
        ConnectedAt = DateTime.UtcNow;
    }

    public void SetTranscript(string transcriptText)
    {
        if (string.IsNullOrWhiteSpace(transcriptText))
            throw new ArgumentException("Transcript text is required.", nameof(transcriptText));

        TranscriptText = transcriptText;
        TranscriptAt   = DateTime.UtcNow;
    }

    public void MarkEnded(int durationSeconds)
    {
        if (IsEnded)
            throw new InvalidOperationException("Call is already ended.");

        IsEnded         = true;
        DurationSeconds = durationSeconds;
        EndedAt         = DateTime.UtcNow;
    }
}
