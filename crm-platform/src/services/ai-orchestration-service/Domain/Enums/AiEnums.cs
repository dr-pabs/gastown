namespace CrmPlatform.AiOrchestrationService.Domain.Enums;

/// <summary>AI capability being exercised.</summary>
public enum CapabilityType
{
    LeadScoring            = 1,
    EmailDraft             = 2,
    CaseSummarisation      = 3,
    SentimentAnalysis      = 4,
    NextBestAction         = 5,
    JourneyPersonalisation = 6,
    SmsComposition         = 7,
    TeamsNotification      = 8,
    TeamsCall              = 9
}

/// <summary>Lifecycle state of an async AI job.</summary>
public enum AiJobStatus
{
    Queued     = 1,
    InProgress = 2,
    Succeeded  = 3,
    Failed     = 4,
    Abandoned  = 5
}

/// <summary>Sentiment classification returned by the model.</summary>
public enum SentimentLabel
{
    Positive = 1,
    Neutral  = 2,
    Negative = 3,
    Mixed    = 4
}

/// <summary>
/// Business use-case within a capability — used to select the correct prompt template.
/// E.g. EmailDraft+LeadAssigned vs EmailDraft+OpportunityWon.
/// </summary>
public enum UseCase
{
    // Generic / not further qualified
    Default                   = 0,

    // LeadScoring contexts
    LeadCreated               = 10,
    LeadAssigned              = 11,

    // EmailDraft contexts
    EmailDraftLeadAssigned    = 20,
    EmailDraftOpportunityWon  = 21,
    EmailDraftCaseFollowUp    = 22,

    // CaseSummarisation contexts
    CaseResolved              = 30,
    CaseOnDemand              = 31,

    // SentimentAnalysis contexts
    CaseCommentAdded          = 40,

    // NextBestAction contexts
    NbaLeadAssigned           = 50,
    NbaOpportunityStageChanged= 51,

    // JourneyPersonalisation contexts
    JourneyEnrollmentCreated  = 60,

    // SmsComposition contexts
    SmsBroadcast              = 70,
    SmsFollowUp               = 71,

    // Teams contexts
    TeamsAdaptiveCard         = 80,
    TeamsOutboundCall         = 81
}
