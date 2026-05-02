using CrmPlatform.CssService.Domain.Enums;

namespace CrmPlatform.CssService.Api.Dtos;

// ─── Case DTOs ────────────────────────────────────────────────────────────────

public sealed record CreateCaseRequest(
    string       Title,
    string       Description,
    CasePriority Priority,
    CaseChannel  Channel,
    Guid?        ContactId,
    Guid?        AccountId);

public sealed record TransitionStatusRequest(CaseStatus NewStatus);

public sealed record AssignCaseRequest(Guid AssignedToUserId);

public sealed record EscalateCaseRequest(string Reason, Guid? NewAssigneeId);

public sealed record AddCommentRequest(
    string           Body,
    bool             IsInternal,
    CommentAuthorType AuthorType);

public sealed record CaseResponse(
    Guid         Id,
    string       Title,
    string       Description,
    string       Status,
    string       Priority,
    string       Channel,
    Guid?        ContactId,
    Guid?        AccountId,
    Guid?        AssignedToUserId,
    DateTime?    SlaDeadline,
    bool         SlaBreached,
    DateTime     CreatedAt,
    DateTime     UpdatedAt);

public sealed record PagedCasesResponse(
    IReadOnlyList<CaseResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

// ─── Customer headless contract DTOs ───────────────────────────────────────────

public sealed record CustomerCreateCaseRequest(
    string Subject,
    string? Description,
    CasePriority Priority,
    string? Category);

public sealed record CustomerAddCommentRequest(string Body);

public sealed record CustomerCaseResponse(
    Guid Id,
    Guid TenantId,
    Guid? ContactId,
    Guid? AccountId,
    Guid? OwnerId,
    string Subject,
    string? Description,
    string Status,
    string Priority,
    string? Category,
    bool SlaBreached,
    DateTime? SlaDueAt,
    string? Sentiment,
    DateTime? ResolvedAt,
    bool IsDeleted,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CustomerPagedCasesResponse(
    IReadOnlyList<CustomerCaseResponse> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);

// ─── Comment DTOs ─────────────────────────────────────────────────────────────

public sealed record CommentResponse(
    Guid     Id,
    Guid     CaseId,
    Guid     AuthorId,
    string   AuthorType,
    string   Body,
    bool     IsInternal,
    DateTime CreatedAt);

public sealed record CustomerCommentResponse(
    Guid Id,
    Guid CaseId,
    Guid AuthorId,
    string? AuthorName,
    string Body,
    bool IsInternal,
    DateTime CreatedAt);

// ─── SLA Policy DTOs ──────────────────────────────────────────────────────────

public sealed record CreateSlaPolicyRequest(
    string       Name,
    CasePriority Priority,
    int          FirstResponseMinutes,
    int          ResolutionMinutes,
    bool         BusinessHoursOnly);

public sealed record SlaPolicyResponse(
    Guid         Id,
    string       Name,
    string       Priority,
    int          FirstResponseMinutes,
    int          ResolutionMinutes,
    bool         BusinessHoursOnly,
    DateTime     CreatedAt);

// ─── Shared ───────────────────────────────────────────────────────────────────

public sealed record CreatedResponse(Guid Id);
