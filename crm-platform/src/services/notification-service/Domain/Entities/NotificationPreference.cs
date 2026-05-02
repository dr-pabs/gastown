using CrmPlatform.NotificationService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.NotificationService.Domain.Entities;

/// <summary>
/// Per-user per-channel per-category opt-in/out preference.
/// Default is enabled — a missing record means enabled.
/// </summary>
public sealed class NotificationPreference : BaseEntity
{
    public Guid                 UserId    { get; private set; }
    public NotificationChannel  Channel   { get; private set; }
    public NotificationCategory Category  { get; private set; }
    public bool                 IsEnabled { get; private set; }

    private NotificationPreference() { }

    public static NotificationPreference Create(
        Guid                tenantId,
        Guid                userId,
        NotificationChannel  channel,
        NotificationCategory category,
        bool                 isEnabled)
    {
        return new NotificationPreference
        {
            Id        = Guid.NewGuid(),
            TenantId  = tenantId,
            UserId    = userId,
            Channel   = channel,
            Category  = category,
            IsEnabled = isEnabled
        };
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
    }
}
