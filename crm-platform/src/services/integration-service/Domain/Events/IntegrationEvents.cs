using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.IntegrationService.Domain.Events;

/// <summary>
/// All events published to topic: crm.integrations
/// </summary>

// ─── Inbound ──────────────────────────────────────────────────────────────────

/// <summary>
/// Fired when an inbound webhook has been validated, logged, and translated.
/// Other services subscribe to act on the normalised external event.
/// </summary>
public sealed record ExternalEventReceivedEvent(
    Guid   TenantId,
    Guid   InboundEventId,
    string ConnectorType,        // e.g. "HubSpot"
    string NormalisedEventType,  // e.g. "hubspot.contact.updated"
    string PayloadSummary        // Non-PII summary only — e.g. "ContactId:12345"
) : IDomainEvent
{
    public Guid     EventId    { get; } = Guid.NewGuid();
    public string   EventType  => "external.event.received";
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// ─── Outbound ────────────────────────────────────────────────────────────────

/// <summary>
/// Fired when an outbound job has been abandoned (retry window exceeded).
/// notification-service subscribes to alert the tenant admin.
/// </summary>
public sealed record OutboundJobFailedEvent(
    Guid   TenantId,
    Guid   OutboundJobId,
    string ConnectorType,
    string EventType,
    string FailureReason,
    int    AttemptCount
) : IDomainEvent
{
    public Guid     EventId    { get; } = Guid.NewGuid();
    public string   EventType  => "outbound.job.failed";
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// ─── Connector lifecycle ──────────────────────────────────────────────────────

public sealed record ConnectorDisconnectedEvent(
    Guid   TenantId,
    Guid   ConnectorConfigId,
    string ConnectorType,
    string Reason
) : IDomainEvent
{
    public Guid     EventId    { get; } = Guid.NewGuid();
    public string   EventType  => "connector.disconnected";
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
