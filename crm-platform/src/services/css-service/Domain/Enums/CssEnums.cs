namespace CrmPlatform.CssService.Domain.Enums;

public enum CaseStatus
{
    New,
    Open,
    Pending,
    Escalated,
    Resolved,
    Closed
}

public enum CasePriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum CaseChannel
{
    Email,
    Phone,
    Portal,
    Chat,
    Api
}

public enum CommentAuthorType
{
    Staff,
    Customer
}
