using CrmPlatform.AiOrchestrationService.Domain.Enums;

namespace CrmPlatform.AiOrchestrationService.Domain.Events;

// ── Events published to crm.ai topic ────────────────────────────────────────

/// <summary>Published after a lead's AI score has been calculated.</summary>
public sealed record LeadScoredEvent(
    Guid   TenantId,
    Guid   LeadId,
    Guid   JobId,
    int    AiScore,
    string ScoreRationale,
    double Confidence);

/// <summary>Published after a case summary has been generated.</summary>
public sealed record CaseSummarisedEvent(
    Guid   TenantId,
    Guid   CaseId,
    Guid   JobId,
    string Summary);

/// <summary>Published after sentiment analysis on a case comment.</summary>
public sealed record SentimentAnalysedEvent(
    Guid           TenantId,
    Guid           CaseId,
    Guid           CommentId,
    Guid           JobId,
    SentimentLabel Sentiment,
    double         Score);

/// <summary>Published after a next-best-action recommendation is generated.</summary>
public sealed record NextBestActionGeneratedEvent(
    Guid   TenantId,
    Guid   EntityId,
    string EntityType,
    Guid   JobId,
    string Action,
    string Rationale);

/// <summary>Published after a personalised journey branch has been recommended.</summary>
public sealed record JourneyPersonalisedEvent(
    Guid   TenantId,
    Guid   EnrollmentId,
    Guid   JobId,
    Guid   RecommendedBranchId,
    string Rationale);

/// <summary>Published when a Teams call transcript is ready.</summary>
public sealed record TeamsCallTranscriptReadyEvent(
    Guid   TenantId,
    Guid   CallRecordId,
    string TargetUserId,
    string TranscriptText,
    int    DurationSeconds);

/// <summary>Published when all retry attempts for an async AI job are exhausted.</summary>
public sealed record AiJobFailedEvent(
    Guid           TenantId,
    Guid           JobId,
    CapabilityType CapabilityType,
    UseCase        UseCase,
    Guid?          RequestedByUserId,
    string         Reason);

/// <summary>Published when an async AI job has not been picked up within its TTL (1 hour).</summary>
public sealed record AiJobStalledEvent(
    Guid           TenantId,
    Guid           JobId,
    CapabilityType CapabilityType,
    UseCase        UseCase,
    Guid?          RequestedByUserId,
    DateTime       QueuedAt);

// ── Events consumed FROM other topics ────────────────────────────────────────

// crm.sfa
public sealed record LeadCreatedEvent(Guid TenantId, Guid LeadId, string LeadData);
public sealed record LeadAssignedEvent(Guid TenantId, Guid LeadId, Guid AssignedToUserId, string LeadData);
public sealed record OpportunityStageChangedEvent(Guid TenantId, Guid OpportunityId, string OldStage, string NewStage, string OpportunityData);

// crm.css
public sealed record CaseResolvedEvent(Guid TenantId, Guid CaseId, string CaseSummaryData);
public sealed record CaseCommentAddedEvent(Guid TenantId, Guid CaseId, Guid CommentId, string CommentText);

// crm.marketing
public sealed record JourneyEnrollmentCreatedEvent(Guid TenantId, Guid EnrollmentId, Guid ContactId, Guid JourneyId, string EnrollmentData);

// crm.platform
public sealed record TenantProvisionedEvent(Guid TenantId, string TenantName);
public sealed record TenantSuspendedEvent(Guid TenantId);
