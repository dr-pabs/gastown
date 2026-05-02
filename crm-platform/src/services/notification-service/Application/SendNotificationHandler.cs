using CrmPlatform.NotificationService.Domain.Entities;
using CrmPlatform.NotificationService.Domain.Enums;
using CrmPlatform.NotificationService.Domain.Events;
using CrmPlatform.NotificationService.Infrastructure.Acs;
using CrmPlatform.NotificationService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrmPlatform.NotificationService.Application;

// ──────────────────────────────────────────────────────────────────────────────
// Command
// ──────────────────────────────────────────────────────────────────────────────
public sealed record SendNotificationCommand(
    Guid                TenantId,
    Guid?               RecipientUserId,
    string              RecipientAddress,
    NotificationChannel  Channel,
    NotificationCategory Category,
    string               BodyPlain,
    string?              Subject     = null,
    string?              BodyHtml    = null,
    Guid?                TemplateId  = null,
    IDictionary<string, object?>? Variables = null);

// ──────────────────────────────────────────────────────────────────────────────
// Handler
// ──────────────────────────────────────────────────────────────────────────────
/// <summary>
/// Core send pipeline:
/// 1. Check preferences — skip if opted out (still records a Skipped entry)
/// 2. Resolve template if TemplateId supplied — render with Handlebars
/// 3. Create NotificationRecord (Queued)
/// 4. Call ACS sender
/// 5. Update record status
/// 6. Publish event to crm.notifications
/// 7. For InApp channel — also create InAppNotification entity
/// </summary>
public sealed class SendNotificationHandler(
    NotificationDbContext db,
    INotificationSender sender,
    ServiceBusEventPublisher publisher,
    ILogger<SendNotificationHandler> logger)
{
    public async Task<Result<Guid>> HandleAsync(
        SendNotificationCommand command,
        CancellationToken ct = default)
    {
        // ── 1. Preference check ───────────────────────────────────────────────
        if (command.RecipientUserId.HasValue
            && command.Channel != NotificationChannel.InApp)  // InApp always sent
        {
            var optedOut = await IsOptedOutAsync(
                command.TenantId, command.RecipientUserId.Value,
                command.Channel, command.Category, ct);

            if (optedOut)
            {
                var skipped = NotificationRecord.CreateSkipped(
                    command.TenantId, command.RecipientUserId,
                    command.RecipientAddress, command.Channel, command.Category);

                db.Records.Add(skipped);
                await db.SaveChangesAsync(ct);

                logger.LogInformation(
                    "Notification skipped — user {UserId} opted out of {Channel}/{Category}",
                    command.RecipientUserId, command.Channel, command.Category);

                return Result.Ok(skipped.Id);
            }
        }

        // ── 2. Template resolution ────────────────────────────────────────────
        string subject   = command.Subject   ?? string.Empty;
        string bodyPlain = command.BodyPlain;
        string? bodyHtml = command.BodyHtml;

        if (command.TemplateId.HasValue)
        {
            var template = await db.Templates
                .FirstOrDefaultAsync(t => t.Id == command.TemplateId.Value, ct);

            if (template is null)
            {
                logger.LogWarning(
                    "Template {TemplateId} not found — falling back to raw body.",
                    command.TemplateId);
            }
            else
            {
                var vars = command.Variables ?? new Dictionary<string, object?>();
                var rendered = template.Render(vars);
                subject   = rendered.Subject   ?? subject;
                bodyPlain = rendered.BodyPlain;
                bodyHtml  = rendered.BodyHtml  ?? bodyHtml;
            }
        }

        // ── 3. Create record ──────────────────────────────────────────────────
        var record = NotificationRecord.CreateQueued(
            command.TenantId,
            command.RecipientUserId,
            command.RecipientAddress,
            command.Channel,
            command.Category,
            bodyPlain,
            command.TemplateId,
            subject.Length > 0 ? subject : null,
            bodyHtml);

        db.Records.Add(record);

        // ── 4. In-App — create inbox item ─────────────────────────────────────
        if (command.Channel == NotificationChannel.InApp
            && command.RecipientUserId.HasValue)
        {
            var inApp = InAppNotification.Create(
                command.TenantId,
                command.RecipientUserId.Value,
                subject.Length > 0 ? subject : command.Category.ToString(),
                bodyPlain,
                command.Category);

            db.InAppItems.Add(inApp);
            await db.SaveChangesAsync(ct);

            // No ACS call needed for InApp
            record.MarkSent(inApp.Id.ToString());
            await db.SaveChangesAsync(ct);

            await PublishEventAsync(record, ct);
            return Result.Ok(record.Id);
        }

        await db.SaveChangesAsync(ct);

        // ── 5. Send via ACS ───────────────────────────────────────────────────
        try
        {
            var providerId = await sender.SendAsync(record, ct);
            record.MarkSent(providerId);

            await PublishEventAsync(record, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ACS send failed for {Channel} to RecipientUserId={UserId}",
                command.Channel, command.RecipientUserId);

            record.MarkFailed(ex.Message);

            await publisher.PublishAsync(
                "crm.notifications",
                new NotificationFailedEvent(
                    record.Id, record.TenantId,
                    record.RecipientUserId, record.Channel,
                    ex.Message),
                ct);
        }

        await db.SaveChangesAsync(ct);
        return Result.Ok(record.Id);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task<bool> IsOptedOutAsync(
        Guid tenantId, Guid userId,
        NotificationChannel channel, NotificationCategory category,
        CancellationToken ct)
    {
        var pref = await db.Preferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.TenantId == tenantId
                && p.UserId   == userId
                && p.Channel  == channel
                && p.Category == category,
                ct);

        // No record = default = enabled
        return pref is not null && !pref.IsEnabled;
    }

    private async Task PublishEventAsync(NotificationRecord record, CancellationToken ct)
    {
        DomainEvent evt = record.Status == NotificationStatus.Sent
            ? new NotificationSentEvent(
                record.Id, record.TenantId,
                record.RecipientUserId, record.Channel,
                record.ProviderMessageId!)
            : new NotificationDeliveredEvent(
                record.Id, record.TenantId,
                record.RecipientUserId, record.Channel);

        await publisher.PublishAsync("crm.notifications", evt, ct);
    }
}
