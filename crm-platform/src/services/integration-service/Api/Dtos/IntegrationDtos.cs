using CrmPlatform.IntegrationService.Domain.Enums;

namespace CrmPlatform.IntegrationService.Api.Dtos;

// ─── Connector ────────────────────────────────────────────────────────────────

public sealed record CreateConnectorRequest(
    ConnectorType ConnectorType,
    string        Label,
    RetryPolicyDto? RetryPolicy);

public sealed record RetryPolicyDto(
    int    MaxRetryDurationMinutes,
    int    InitialRetryDelaySeconds,
    int    MaxRetryDelaySeconds,
    double BackoffMultiplier);

public sealed record ConnectorResponse(
    Guid            Id,
    ConnectorType   ConnectorType,
    string          Label,
    ConnectorStatus Status,
    string?         ExternalAccountId,
    string?         OAuthScopes,
    DateTime?       TokenExpiresUtc,
    bool            HasWebhookSecret,
    RetryPolicyDto  RetryPolicy,
    DateTime        CreatedAt,
    DateTime        ModifiedAt);

public sealed record AuthorizationUrlResponse(string AuthorizationUrl);

// ─── Outbound Jobs ────────────────────────────────────────────────────────────

public sealed record OutboundJobResponse(
    Guid             Id,
    Guid             ConnectorConfigId,
    ConnectorType    ConnectorType,
    string           EventType,
    OutboundJobStatus Status,
    int              AttemptCount,
    DateTime?        FirstAttemptAt,
    DateTime?        LastAttemptAt,
    DateTime?        NextRetryAt,
    DateTime?        AbandonedAt,
    string?          FailureReason,
    string?          ExternalId,
    DateTime         CreatedAt);

public sealed record PagedOutboundJobsResponse(
    IReadOnlyList<OutboundJobResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

// ─── Inbound Events ───────────────────────────────────────────────────────────

public sealed record InboundEventResponse(
    Guid               Id,
    ConnectorType      ConnectorType,
    string?            ExternalEventId,
    string?            NormalisedEventType,
    InboundEventStatus Status,
    string?            ServiceBusMessageId,
    string?            FailureReason,
    DateTime           ReceivedAt,
    DateTime?          ProcessedAt);

/// <summary>Detail view includes the raw payload (for debugging, admin only).</summary>
public sealed record InboundEventDetailResponse(
    Guid               Id,
    ConnectorType      ConnectorType,
    string?            ExternalEventId,
    string             RawPayload,
    string?            NormalisedEventType,
    InboundEventStatus Status,
    string?            ServiceBusMessageId,
    string?            FailureReason,
    DateTime           ReceivedAt,
    DateTime?          ProcessedAt);

public sealed record PagedInboundEventsResponse(
    IReadOnlyList<InboundEventResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

// ─── Admin ────────────────────────────────────────────────────────────────────

public sealed record AdminConnectorResponse(
    Guid            Id,
    Guid            TenantId,
    ConnectorType   ConnectorType,
    string          Label,
    ConnectorStatus Status,
    string?         ExternalAccountId,
    DateTime        CreatedAt);
