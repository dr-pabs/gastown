using CrmPlatform.NotificationService.Domain.Entities;
using CrmPlatform.NotificationService.Domain.Enums;
using CrmPlatform.NotificationService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrmPlatform.NotificationService.Application;

// ──────────────────────────────────────────────────────────────────────────────
// Template management
// ──────────────────────────────────────────────────────────────────────────────

public sealed record CreateTemplateCommand(
    string               Name,
    NotificationCategory Category,
    NotificationChannel  Channel,
    string               BodyPlain,
    string?              SubjectTemplate  = null,
    string?              BodyHtmlTemplate = null);

public sealed record UpdateTemplateCommand(
    Guid    TemplateId,
    string  BodyPlain,
    string? SubjectTemplate  = null,
    string? BodyHtmlTemplate = null);

public sealed class TemplateHandler(
    NotificationDbContext db,
    ITenantContext tenant,
    ILogger<TemplateHandler> logger)
{
    public async Task<Result<Guid>> HandleCreateAsync(
        CreateTemplateCommand command, CancellationToken ct = default)
    {
        // Prevent duplicate name per tenant+channel+category
        var exists = await db.Templates.AnyAsync(
            t => t.TenantId == tenant.TenantId
              && t.Name     == command.Name.Trim()
              && t.Channel  == command.Channel
              && t.Category == command.Category,
            ct);

        if (exists)
            return Result.Fail<Guid>(
                "A template with this name/channel/category already exists.",
                ResultErrorCode.Conflict);

        var template = NotificationTemplate.Create(
            tenant.TenantId,
            command.Name,
            command.Category,
            command.Channel,
            command.BodyPlain,
            command.SubjectTemplate,
            command.BodyHtmlTemplate);

        db.Templates.Add(template);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Template {TemplateId} '{Name}' created for tenant {TenantId}",
            template.Id, template.Name, tenant.TenantId);

        return Result.Ok(template.Id);
    }

    public async Task<Result<bool>> HandleUpdateAsync(
        UpdateTemplateCommand command, CancellationToken ct = default)
    {
        var template = await db.Templates
            .FirstOrDefaultAsync(t => t.Id == command.TemplateId, ct);

        if (template is null)
            return Result.Fail<bool>("Template not found.", ResultErrorCode.NotFound);

        try
        {
            template.UpdateContent(
                command.BodyPlain,
                command.SubjectTemplate,
                command.BodyHtmlTemplate);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail<bool>(ex.Message, ResultErrorCode.ValidationError);
        }

        await db.SaveChangesAsync(ct);
        return Result.Ok(true);
    }

    public async Task<Result<bool>> HandleActivateAsync(Guid templateId, CancellationToken ct = default)
    {
        var template = await db.Templates
            .FirstOrDefaultAsync(t => t.Id == templateId, ct);

        if (template is null)
            return Result.Fail<bool>("Template not found.", ResultErrorCode.NotFound);

        // Deactivate any existing active template for this channel+category
        var existing = await db.Templates
            .Where(t =>
                t.Id       != templateId
                && t.TenantId == tenant.TenantId
                && t.Channel  == template.Channel
                && t.Category == template.Category
                && t.IsActive == true)
            .ToListAsync(ct);

        foreach (var old in existing)
            old.Deactivate();

        template.Activate();
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Template {TemplateId} activated. {Count} previous version(s) deactivated.",
            templateId, existing.Count);

        return Result.Ok(true);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Preference management
// ──────────────────────────────────────────────────────────────────────────────

public sealed record UpsertPreferenceItem(
    NotificationChannel  Channel,
    NotificationCategory Category,
    bool                 IsEnabled);

public sealed class PreferenceHandler(
    NotificationDbContext db,
    ITenantContext tenant)
{
    public async Task<Result<bool>> HandleUpsertAsync(
        Guid userId,
        IReadOnlyList<UpsertPreferenceItem> items,
        CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            var pref = await db.Preferences
                .FirstOrDefaultAsync(p =>
                    p.TenantId == tenant.TenantId
                    && p.UserId   == userId
                    && p.Channel  == item.Channel
                    && p.Category == item.Category,
                    ct);

            if (pref is null)
            {
                pref = NotificationPreference.Create(
                    tenant.TenantId, userId,
                    item.Channel, item.Category, item.IsEnabled);
                db.Preferences.Add(pref);
            }
            else
            {
                pref.SetEnabled(item.IsEnabled);
            }
        }

        await db.SaveChangesAsync(ct);
        return Result.Ok(true);
    }
}
