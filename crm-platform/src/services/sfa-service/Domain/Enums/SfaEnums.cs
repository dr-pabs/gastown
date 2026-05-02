namespace CrmPlatform.SfaService.Domain.Enums;

public enum LeadStatus
{
    New,
    Contacted,
    Qualified,
    Converted,
    Disqualified,
}

public enum OpportunityStage
{
    Qualify    = 1,
    Propose    = 2,
    Negotiate  = 3,
    Won        = 4,
    Lost       = 5,
}

public enum QuoteStatus
{
    Draft,
    Sent,
    Accepted,
    Rejected,
}

public enum ActivityType
{
    Call,
    Email,
    Meeting,
    Note,
}

public enum LeadSource
{
    Web,
    Referral,
    Campaign,
    InboundCall,
    Partner,
    Other,
}

public enum AccountSize
{
    Micro,     // 1-9
    Small,     // 10-49
    Medium,    // 50-249
    Large,     // 250-999
    Enterprise, // 1000+
}
