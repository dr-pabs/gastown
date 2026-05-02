using CrmPlatform.AiOrchestrationService.Domain.Entities;
using CrmPlatform.AiOrchestrationService.Domain.Enums;

namespace CrmPlatform.AiOrchestrationService.Api.Dtos;

// ── Request DTOs ──────────────────────────────────────────────────────────────

public sealed record EmailDraftRequest(
    string  LeadName,
    string  Company,
    string? ProductInterest,
    UseCase UseCase = UseCase.EmailDraftLeadAssigned);

public sealed record TeamsNotificationRequest(
    string CardTitle,
    string Context);

public sealed record TeamsCallRequest(
    string TargetAcsUserId,
    string CallContext);

public sealed record SmsComposeRequest(
    string  RecipientPhone,
    string  FromPhone,
    string  CampaignName,
    string  Goal,
    UseCase UseCase = UseCase.SmsBroadcast);

public sealed record CaseSummarisationRequest(
    Guid   CaseId,
    string CaseData);

public sealed record UpsertPromptTemplateRequest(
    CapabilityType CapabilityType,
    UseCase        UseCase,
    string         SystemPrompt,
    string         UserPromptTemplate);

// ── Response DTOs ─────────────────────────────────────────────────────────────

public sealed record EmailDraftResponse(
    Guid   ResultId,
    string DraftContent);

public sealed record TeamsNotificationResponse(
    Guid ResultId,
    bool Sent);

public sealed record TeamsCallResponse(
    Guid    CallRecordId,
    string? AcsCallId);

public sealed record AiJobResponse(
    Guid           Id,
    CapabilityType CapabilityType,
    UseCase        UseCase,
    AiJobStatus    Status,
    int            AttemptCount,
    string?        FailureReason,
    DateTime       CreatedAt,
    DateTime?      CompletedAt,
    Guid?          AiResultId)
{
    public static AiJobResponse From(AiJob j) => new(
        j.Id, j.CapabilityType, j.UseCase, j.Status,
        j.AttemptCount, j.FailureReason, j.CreatedAt, j.CompletedAt, j.AiResultId);
}

public sealed record AiResultResponse(
    Guid           Id,
    CapabilityType CapabilityType,
    UseCase        UseCase,
    string         ModelName,
    string         OutputContent,
    int            InputTokens,
    int            OutputTokens,
    DateTime       RecordedAt)
{
    public static AiResultResponse From(AiResult r) => new(
        r.Id, r.CapabilityType, r.UseCase, r.ModelName,
        r.OutputContent, r.InputTokens, r.OutputTokens, r.RecordedAt);
}

public sealed record PromptTemplateResponse(
    Guid           Id,
    CapabilityType CapabilityType,
    UseCase        UseCase,
    string         SystemPrompt,
    string         UserPromptTemplate,
    DateTime       CreatedAt,
    DateTime?      UpdatedAt)
{
    public static PromptTemplateResponse From(PromptTemplate p) => new(
        p.Id, p.CapabilityType, p.UseCase,
        p.SystemPrompt, p.UserPromptTemplate,
        p.CreatedAt, p.UpdatedAt);
}

public sealed record SmsRecordResponse(
    Guid     Id,
    string   RecipientPhone,
    bool     IsSent,
    bool     IsFailed,
    string?  AcsMessageId,
    DateTime CreatedAt)
{
    public static SmsRecordResponse From(SmsRecord s) => new(
        s.Id, s.RecipientPhone, s.IsSent, s.IsFailed, s.AcsMessageId, s.CreatedAt);
}
