using CrmPlatform.AnalyticsService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.AnalyticsService.Domain.Entities;

/// <summary>
/// Raw event record — an immutable append-only log of every domain event
/// received from all service bus topics. The source of truth for all analytics.
/// Never updated or soft-deleted — retains full history for audit and replay.
/// </summary>
public sealed class AnalyticsEvent : BaseEntity
{
    public EventSource Source       { get; private set; }
    public string      EventType    { get; private set; } = string.Empty;
    public string      SourceId     { get; private set; } = string.Empty;  // originating entity Id
    public string      PayloadJson  { get; private set; } = string.Empty;
    public DateTime    OccurredAt   { get; private set; }

    private AnalyticsEvent() { } // EF Core

    public static AnalyticsEvent Record(
        Guid       tenantId,
        EventSource source,
        string     eventType,
        string     sourceId,
        string     payloadJson,
        DateTime   occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        return new AnalyticsEvent
        {
            Id          = Guid.NewGuid(),
            TenantId    = tenantId,
            Source      = source,
            EventType   = eventType,
            SourceId    = sourceId,
            PayloadJson = payloadJson,
            OccurredAt  = occurredAt
        };
    }
}
