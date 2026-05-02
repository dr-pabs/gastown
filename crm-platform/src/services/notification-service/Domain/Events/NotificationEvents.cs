using CrmPlatform.NotificationService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.NotificationService.Domain.Events;

// ─── Outbound events published to crm.notifications ─────────────────────────

public sealed record NotificationSentEvent(
    Guid                RecordId,
    Guid                TenantId,
    Guid?               RecipientUserId,
    NotificationChannel Channel,
    string              ProviderMessageId)
    : DomainEvent(TenantId)
{
    public override string EventType => "notification.sent";
}

public sealed record NotificationFailedEvent(
    Guid                RecordId,
    Guid                TenantId,
    Guid?               RecipientUserId,
    NotificationChannel Channel,
    string              FailureReason)
    : DomainEvent(TenantId)
{
    public override string EventType => "notification.failed";
}

public sealed record NotificationDeliveredEvent(
    Guid                RecordId,
    Guid                TenantId,
    Guid?               RecipientUserId,
    NotificationChannel Channel)
    : DomainEvent(TenantId)
{
    public override string EventType => "notification.delivered";
}
