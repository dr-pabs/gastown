using CrmPlatform.NotificationService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.NotificationService.Domain.Entities;

/// <summary>
/// Append-only delivery record. Never mutated after creation — status updates
/// are applied via UpdateStatus() which sets timestamped fields.
/// </summary>
public sealed class NotificationRecord : BaseEntity
{
    public Guid?                RecipientUserId   { get; private set; }
    /// <summary>Email address, phone number, or device push token. NOT logged to telemetry.</summary>
    public string               RecipientAddress  { get; private set; } = string.Empty;
    public NotificationChannel  Channel           { get; private set; }
    public NotificationCategory Category          { get; private set; }
    public Guid?                TemplateId        { get; private set; }
    public string?              Subject           { get; private set; }
    public string?              BodyHtml          { get; private set; }
    public string               BodyPlain         { get; private set; } = string.Empty;
    public NotificationStatus   Status            { get; private set; }
    public string?              ProviderMessageId { get; private set; }
    public string?              FailureReason     { get; private set; }
    public DateTime?            SentAt            { get; private set; }
    public DateTime?            DeliveredAt       { get; private set; }
    public DateTime?            OpenedAt          { get; private set; }
    public DateTime?            ClickedAt         { get; private set; }

    private NotificationRecord() { }

    public static NotificationRecord CreateQueued(
        Guid                tenantId,
        Guid?               recipientUserId,
        string              recipientAddress,
        NotificationChannel  channel,
        NotificationCategory category,
        string               bodyPlain,
        Guid?                templateId  = null,
        string?              subject     = null,
        string?              bodyHtml    = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPlain);

        return new NotificationRecord
        {
            Id               = Guid.NewGuid(),
            TenantId         = tenantId,
            RecipientUserId  = recipientUserId,
            RecipientAddress = recipientAddress,
            Channel          = channel,
            Category         = category,
            TemplateId       = templateId,
            Subject          = subject,
            BodyHtml         = bodyHtml,
            BodyPlain        = bodyPlain,
            Status           = NotificationStatus.Queued
        };
    }

    public static NotificationRecord CreateSkipped(
        Guid                tenantId,
        Guid?               recipientUserId,
        string              recipientAddress,
        NotificationChannel  channel,
        NotificationCategory category)
    {
        return new NotificationRecord
        {
            Id               = Guid.NewGuid(),
            TenantId         = tenantId,
            RecipientUserId  = recipientUserId,
            RecipientAddress = recipientAddress,
            Channel          = channel,
            Category         = category,
            BodyPlain        = string.Empty,
            Status           = NotificationStatus.Skipped
        };
    }

    public void MarkSent(string providerMessageId)
    {
        Status            = NotificationStatus.Sent;
        ProviderMessageId = providerMessageId;
        SentAt            = DateTime.UtcNow;
    }

    public void MarkFailed(string reason)
    {
        Status        = NotificationStatus.Failed;
        FailureReason = reason;
    }

    public void UpdateStatus(DeliveryWebhookEvent webhookEvent)
    {
        var now = DateTime.UtcNow;
        switch (webhookEvent)
        {
            case DeliveryWebhookEvent.Delivered:
                Status      = NotificationStatus.Delivered;
                DeliveredAt = now;
                break;
            case DeliveryWebhookEvent.Failed:
                Status = NotificationStatus.Failed;
                break;
            case DeliveryWebhookEvent.Bounced:
                Status = NotificationStatus.Bounced;
                break;
            case DeliveryWebhookEvent.Opened:
                Status   = NotificationStatus.Opened;
                OpenedAt = now;
                break;
            case DeliveryWebhookEvent.Clicked:
                Status    = NotificationStatus.Clicked;
                ClickedAt = now;
                break;
        }
    }
}
