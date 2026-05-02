using CrmPlatform.MarketingService.Domain.Enums;

namespace CrmPlatform.MarketingService.Api.Dtos;

// ── Campaigns ─────────────────────────────────────────────────────────────────

public sealed record CreateCampaignRequest(
    string          Name,
    string          Description,
    CampaignChannel Channel);

public sealed record CreateCampaignResponse(Guid CampaignId, string Name);

public sealed record TransitionCampaignRequest(
    string    Action,
    DateTime? ScheduledAt);

public sealed record CampaignSummaryResponse(
    Guid            CampaignId,
    string          Name,
    CampaignChannel Channel,
    CampaignStatus  Status,
    DateTime?       ScheduledAt,
    DateTime?       StartedAt,
    DateTime?       EndedAt,
    DateTime        CreatedAt);

// ── Journeys ──────────────────────────────────────────────────────────────────

public sealed record CreateJourneyRequest(
    Guid   CampaignId,
    string Name,
    string Description);

public sealed record CreateJourneyResponse(Guid JourneyId, string Name);

public sealed record SetJourneyStepsRequest(string StepsJson, int StepCount);

public sealed record EnrollContactRequest(Guid ContactId);

public sealed record EnrollContactResponse(Guid EnrollmentId);

public sealed record JourneySummaryResponse(
    Guid     JourneyId,
    string   Name,
    bool     IsPublished,
    int      StepCount,
    DateTime CreatedAt);

// ── Email Templates ───────────────────────────────────────────────────────────

public sealed record CreateEmailTemplateRequest(
    string         Name,
    string         Subject,
    string         HtmlBody,
    string         PlainTextBody,
    TemplateEngine Engine);

public sealed record CreateEmailTemplateResponse(Guid TemplateId, string Name);

public sealed record EmailTemplateSummaryResponse(
    Guid           TemplateId,
    string         Name,
    string         Subject,
    TemplateEngine Engine,
    int            Version,
    bool           IsPublished,
    DateTime       CreatedAt);
