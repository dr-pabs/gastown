namespace CrmPlatform.NotificationService.Domain.Enums;

public enum NotificationChannel
{
    Email   = 1,
    Sms     = 2,
    WebPush = 3,
    MobilePush = 4,
    InApp   = 5
}

public enum NotificationCategory
{
    // Platform / identity
    TenantWelcome         = 1,
    UserWelcome           = 2,
    // SFA
    LeadAssigned          = 10,
    OpportunityWon        = 11,
    // CS&S
    CaseCreated           = 20,
    CaseAssigned          = 21,
    CaseStatusChanged     = 22,
    SlaWarning            = 23,
    SlaBreached           = 24,
    // Marketing
    JourneyStep           = 30,
    // General
    General               = 99
}

public enum NotificationStatus
{
    Queued    = 0,
    Skipped   = 1,   // user opted out
    Sent      = 2,
    Delivered = 3,
    Failed    = 4,
    Bounced   = 5,
    Opened    = 6,
    Clicked   = 7
}

public enum DeliveryWebhookEvent
{
    Delivered,
    Failed,
    Bounced,
    Opened,
    Clicked
}
