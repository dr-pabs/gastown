using CrmPlatform.IntegrationService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.IntegrationService.Domain.Entities;

/// <summary>
/// Async outbound job — queued when a CRM event should be dispatched to an external system.
/// The OutboundDispatchWorker polls this table and dispatches via the appropriate connector adapter.
/// </summary>
public sealed class OutboundJob : BaseEntity
{
    public Guid            ConnectorConfigId { get; private set; }
    public ConnectorType   ConnectorType     { get; private set; }
    public string          EventType         { get; private set; } = string.Empty;

    /// <summary>JSON payload of the CRM event. NOT logged to telemetry — may contain PII.</summary>
    public string          Payload           { get; private set; } = string.Empty;

    public OutboundJobStatus Status          { get; private set; }
    public int               AttemptCount    { get; private set; }
    public DateTime?         FirstAttemptAt  { get; private set; }
    public DateTime?         LastAttemptAt   { get; private set; }

    /// <summary>Null when terminal (Succeeded or Abandoned). Set by worker using RetryPolicy.</summary>
    public DateTime?         NextRetryAt     { get; private set; }

    public DateTime?         AbandonedAt     { get; private set; }
    public string?           FailureReason   { get; private set; }

    /// <summary>ID returned by the external system on success (e.g. Salesforce record Id).</summary>
    public string?           ExternalId      { get; private set; }

    // Navigation (no lazy loading)
    public ConnectorConfig   ConnectorConfig { get; private set; } = null!;

    private OutboundJob() { } // EF

    public static OutboundJob Create(
        Guid tenantId,
        Guid connectorConfigId,
        ConnectorType connectorType,
        string eventType,
        string payload)
    {
        return new OutboundJob
        {
            TenantId          = tenantId,
            ConnectorConfigId = connectorConfigId,
            ConnectorType     = connectorType,
            EventType         = eventType,
            Payload           = payload,
            Status            = OutboundJobStatus.Queued,
            AttemptCount      = 0,
        };
    }

    public void MarkInProgress()
    {
        Status         = OutboundJobStatus.InProgress;
        AttemptCount  += 1;
        FirstAttemptAt ??= DateTime.UtcNow;
        LastAttemptAt  = DateTime.UtcNow;
    }

    public void MarkSucceeded(string? externalId)
    {
        Status     = OutboundJobStatus.Succeeded;
        ExternalId = externalId;
        NextRetryAt = null;
    }

    /// <param name="reason">Failure reason — do NOT include PII.</param>
    /// <param name="nextRetryAt">Next retry time calculated by the worker from RetryPolicy. Null = no more retries.</param>
    public void MarkFailed(string reason, DateTime? nextRetryAt)
    {
        Status        = OutboundJobStatus.Failed;
        FailureReason = reason;
        NextRetryAt   = nextRetryAt;
    }

    /// <summary>
    /// Called when the retry window (MaxRetryDurationMinutes) has been exceeded.
    /// Caller (worker) must check RetryPolicy.IsWindowExceeded() before calling this.
    /// </summary>
    public void Abandon(string reason)
    {
        Status        = OutboundJobStatus.Abandoned;
        FailureReason = reason;
        AbandonedAt   = DateTime.UtcNow;
        NextRetryAt   = null;
    }

    public bool IsTerminal =>
        Status is OutboundJobStatus.Succeeded or OutboundJobStatus.Abandoned;
}

/// <summary>
/// Audit record of a webhook payload received from an external system.
/// Raw payload stored for debugging — NOT logged to Application Insights.
/// </summary>
public sealed class InboundEvent : BaseEntity
{
    public ConnectorType       ConnectorType        { get; private set; }

    /// <summary>Deduplication key from the external system (e.g. HubSpot event id).</summary>
    public string?             ExternalEventId      { get; private set; }

    /// <summary>Original webhook body as received. NOT logged to telemetry — may contain PII.</summary>
    public string              RawPayload           { get; private set; } = string.Empty;

    /// <summary>Normalised event type after translation (e.g. "hubspot.contact.updated").</summary>
    public string?             NormalisedEventType  { get; private set; }

    public InboundEventStatus  Status               { get; private set; }
    public string?             ServiceBusMessageId  { get; private set; }
    public string?             FailureReason        { get; private set; }
    public DateTime            ReceivedAt           { get; private set; }
    public DateTime?           ProcessedAt          { get; private set; }

    private InboundEvent() { } // EF

    public static InboundEvent Receive(
        Guid tenantId,
        ConnectorType connectorType,
        string? externalEventId,
        string rawPayload)
    {
        return new InboundEvent
        {
            TenantId        = tenantId,
            ConnectorType   = connectorType,
            ExternalEventId = externalEventId,
            RawPayload      = rawPayload,
            Status          = InboundEventStatus.Received,
            ReceivedAt      = DateTime.UtcNow,
        };
    }

    public void SetNormalisedType(string normalisedEventType)
    {
        NormalisedEventType = normalisedEventType;
    }

    public void MarkPublished(string serviceBusMessageId)
    {
        Status              = InboundEventStatus.Published;
        ServiceBusMessageId = serviceBusMessageId;
        ProcessedAt         = DateTime.UtcNow;
    }

    public void MarkFailed(string reason)
    {
        Status        = InboundEventStatus.Failed;
        FailureReason = reason;
        ProcessedAt   = DateTime.UtcNow;
    }

    public void Skip(string reason)
    {
        Status        = InboundEventStatus.Skipped;
        FailureReason = reason;
        ProcessedAt   = DateTime.UtcNow;
    }
}
