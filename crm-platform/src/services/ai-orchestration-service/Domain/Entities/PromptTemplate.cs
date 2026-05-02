using CrmPlatform.AiOrchestrationService.Domain.Enums;

namespace CrmPlatform.AiOrchestrationService.Domain.Entities;

/// <summary>
/// Per-tenant, per-capability, per-use-case custom prompt template.
/// Supports Handlebars syntax in UserPromptTemplate.
/// Fallback: tenant custom → platform hard-coded default (PromptDefaults).
/// </summary>
public sealed class PromptTemplate
{
    // ── Private constructor (EF + factory only) ──────────────────────────────
    private PromptTemplate() { }

    public Guid   Id             { get; private set; }
    public Guid   TenantId       { get; private set; }

    // ── Scope ────────────────────────────────────────────────────────────────
    public CapabilityType CapabilityType { get; private set; }
    public UseCase        UseCase        { get; private set; }

    // ── Prompts ──────────────────────────────────────────────────────────────
    public string SystemPrompt       { get; private set; } = string.Empty;
    public string UserPromptTemplate { get; private set; } = string.Empty;

    // ── Soft-delete / audit ──────────────────────────────────────────────────
    public bool      IsDeleted  { get; private set; }
    public DateTime  CreatedAt  { get; private set; }
    public DateTime? UpdatedAt  { get; private set; }
    public DateTime? DeletedAt  { get; private set; }

    // ── Factory ──────────────────────────────────────────────────────────────
    public static PromptTemplate Create(
        Guid           tenantId,
        CapabilityType capabilityType,
        UseCase        useCase,
        string         systemPrompt,
        string         userPromptTemplate)
    {
        if (string.IsNullOrWhiteSpace(systemPrompt))
            throw new ArgumentException("System prompt is required.", nameof(systemPrompt));
        if (string.IsNullOrWhiteSpace(userPromptTemplate))
            throw new ArgumentException("User prompt template is required.", nameof(userPromptTemplate));

        return new PromptTemplate
        {
            Id                 = Guid.NewGuid(),
            TenantId           = tenantId,
            CapabilityType     = capabilityType,
            UseCase            = useCase,
            SystemPrompt       = systemPrompt,
            UserPromptTemplate = userPromptTemplate,
            CreatedAt          = DateTime.UtcNow
        };
    }

    // ── Mutators ─────────────────────────────────────────────────────────────
    public void Update(string systemPrompt, string userPromptTemplate)
    {
        if (IsDeleted)
            throw new InvalidOperationException("Cannot update a deleted prompt template.");
        if (string.IsNullOrWhiteSpace(systemPrompt))
            throw new ArgumentException("System prompt is required.", nameof(systemPrompt));
        if (string.IsNullOrWhiteSpace(userPromptTemplate))
            throw new ArgumentException("User prompt template is required.", nameof(userPromptTemplate));

        SystemPrompt       = systemPrompt;
        UserPromptTemplate = userPromptTemplate;
        UpdatedAt          = DateTime.UtcNow;
    }

    public void Delete()
    {
        if (IsDeleted) return;
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
    }
}
