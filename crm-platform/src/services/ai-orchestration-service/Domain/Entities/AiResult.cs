using CrmPlatform.AiOrchestrationService.Domain.Enums;

namespace CrmPlatform.AiOrchestrationService.Domain.Entities;

/// <summary>
/// Immutable record of a single AI model invocation and its output.
/// Written once after the model returns; never mutated.
/// </summary>
public sealed class AiResult
{
    // ── Private constructor (EF + factory only) ──────────────────────────────
    private AiResult() { }

    public Guid   Id             { get; private set; }
    public Guid   TenantId       { get; private set; }

    // ── Link back to the async job (null for sync requests) ──────────────────
    public Guid?  JobId          { get; private set; }

    // ── Classification ───────────────────────────────────────────────────────
    public CapabilityType CapabilityType { get; private set; }
    public UseCase        UseCase        { get; private set; }

    // ── Model metadata ───────────────────────────────────────────────────────
    public string  ModelName      { get; private set; } = string.Empty;
    public string  PromptUsed     { get; private set; } = string.Empty;

    // ── Output ───────────────────────────────────────────────────────────────
    public string  OutputContent  { get; private set; } = string.Empty;

    // ── Token usage ──────────────────────────────────────────────────────────
    public int     InputTokens    { get; private set; }
    public int     OutputTokens   { get; private set; }

    // ── Timestamp ────────────────────────────────────────────────────────────
    public DateTime RecordedAt    { get; private set; }

    // ── Factory ──────────────────────────────────────────────────────────────
    public static AiResult Record(
        Guid           tenantId,
        Guid?          jobId,
        CapabilityType capabilityType,
        UseCase        useCase,
        string         modelName,
        string         promptUsed,
        string         outputContent,
        int            inputTokens,
        int            outputTokens)
    {
        if (string.IsNullOrWhiteSpace(outputContent))
            throw new ArgumentException("Output content is required.", nameof(outputContent));

        return new AiResult
        {
            Id             = Guid.NewGuid(),
            TenantId       = tenantId,
            JobId          = jobId,
            CapabilityType = capabilityType,
            UseCase        = useCase,
            ModelName      = modelName,
            PromptUsed     = promptUsed,
            OutputContent  = outputContent,
            InputTokens    = inputTokens,
            OutputTokens   = outputTokens,
            RecordedAt     = DateTime.UtcNow
        };
    }
}
