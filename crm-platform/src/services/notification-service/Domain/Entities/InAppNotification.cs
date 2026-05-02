using CrmPlatform.NotificationService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.NotificationService.Domain.Entities;

/// <summary>
/// In-app notification displayed in the web/mobile inbox.
/// Always created regardless of channel preferences — opt-out is UI-level only.
/// </summary>
public sealed class InAppNotification : BaseEntity
{
    public Guid                 RecipientUserId { get; private set; }
    public string               Title           { get; private set; } = string.Empty;
    public string               Body            { get; private set; } = string.Empty;
    public string?              ActionUrl       { get; private set; }
    public NotificationCategory Category        { get; private set; }
    public bool                 IsRead          { get; private set; }
    public DateTime?            ReadAt          { get; private set; }

    private InAppNotification() { }

    public static InAppNotification Create(
        Guid                tenantId,
        Guid                recipientUserId,
        string              title,
        string              body,
        NotificationCategory category,
        string?              actionUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return new InAppNotification
        {
            Id              = Guid.NewGuid(),
            TenantId        = tenantId,
            RecipientUserId = recipientUserId,
            Title           = title.Trim(),
            Body            = body.Trim(),
            ActionUrl       = actionUrl?.Trim(),
            Category        = category,
            IsRead          = false
        };
    }

    public void MarkRead()
    {
        if (IsRead) return;
        IsRead = true;
        ReadAt = DateTime.UtcNow;
    }
}
