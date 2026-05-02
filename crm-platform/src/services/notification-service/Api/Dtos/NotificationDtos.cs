namespace CrmPlatform.NotificationService.Api.Dtos;

// ─── Templates ────────────────────────────────────────────────────────────────
public sealed record CreateTemplateRequest(
    string  Name,
    string  Category,
    string  Channel,
    string  BodyPlain,
    string? SubjectTemplate  = null,
    string? BodyHtmlTemplate = null);

public sealed record UpdateTemplateRequest(
    string  BodyPlain,
    string? SubjectTemplate  = null,
    string? BodyHtmlTemplate = null);

public sealed record TemplateResponse(
    Guid     Id,
    string   Name,
    string   Category,
    string   Channel,
    string?  SubjectTemplate,
    string?  BodyHtmlTemplate,
    string   BodyPlainTemplate,
    bool     IsActive,
    int      Version,
    DateTime CreatedAt,
    DateTime UpdatedAt);

// ─── Preferences ──────────────────────────────────────────────────────────────
public sealed record PreferenceItem(string Channel, string Category, bool IsEnabled);
public sealed record UpsertPreferencesRequest(IReadOnlyList<PreferenceItem> Preferences);
public sealed record PreferenceResponse(Guid Id, string Channel, string Category, bool IsEnabled);

// ─── In-App Inbox ─────────────────────────────────────────────────────────────
public sealed record InAppNotificationResponse(
    Guid     Id,
    string   Title,
    string   Body,
    string?  ActionUrl,
    string   Category,
    bool     IsRead,
    DateTime? ReadAt,
    DateTime CreatedAt);

public sealed record UnreadCountResponse(int Count);
public sealed record PagedInAppResponse(
    IReadOnlyList<InAppNotificationResponse> Items,
    int Total, int Page, int PageSize);

// ─── Internal send ────────────────────────────────────────────────────────────
public sealed record SendNotificationRequest(
    Guid    TenantId,
    Guid?   RecipientUserId,
    string  RecipientAddress,
    string  Channel,
    string  Category,
    string  BodyPlain,
    string? Subject          = null,
    string? BodyHtml         = null,
    Guid?   TemplateId       = null,
    Dictionary<string, object?>? Variables = null);

// ─── ACS Webhook ──────────────────────────────────────────────────────────────
public sealed record AcsWebhookEvent(
    string EventType,
    string MessageId,
    string? DeliveryStatus);

// ─── Shared ───────────────────────────────────────────────────────────────────
public sealed record CreatedResponse(Guid Id);
public sealed record PagedTemplatesResponse(
    IReadOnlyList<TemplateResponse> Items, int Total, int Page, int PageSize);
