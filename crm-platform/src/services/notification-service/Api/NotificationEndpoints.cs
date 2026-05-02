using CrmPlatform.NotificationService.Api.Dtos;
using CrmPlatform.NotificationService.Application;
using CrmPlatform.NotificationService.Domain.Enums;
using CrmPlatform.NotificationService.Infrastructure.Acs;
using CrmPlatform.NotificationService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace CrmPlatform.NotificationService.Api;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var templates    = app.MapGroup("/notification-templates").RequireAuthorization();
        var preferences  = app.MapGroup("/notification-preferences").RequireAuthorization();
        var inbox        = app.MapGroup("/notifications").RequireAuthorization();
        var internalGrp  = app.MapGroup("/internal/notifications");
        var webhooks     = app.MapGroup("/webhooks");

        // ─── Templates ────────────────────────────────────────────────────────

        templates.MapGet("/", async (
            NotificationDbContext db,
            string? channel  = null,
            string? category = null,
            int page     = 1,
            int pageSize = 25) =>
        {
            pageSize = Math.Min(pageSize, 100);
            var query = db.Templates.AsQueryable();

            if (Enum.TryParse<NotificationChannel>(channel, true, out var ch))
                query = query.Where(t => t.Channel == ch);
            if (Enum.TryParse<NotificationCategory>(category, true, out var cat))
                query = query.Where(t => t.Category == cat);

            var total = await query.CountAsync();
            var items = await query
                .OrderBy(t => t.Name)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync();

            return Results.Ok(new PagedTemplatesResponse(
                items.Select(ToTemplateResponse).ToList(), total, page, pageSize));
        });

        templates.MapGet("/{id:guid}", async (Guid id, NotificationDbContext db) =>
        {
            var t = await db.Templates.FirstOrDefaultAsync(x => x.Id == id);
            return t is null ? Results.NotFound() : Results.Ok(ToTemplateResponse(t));
        });

        templates.MapPost("/", async (
            CreateTemplateRequest req,
            TemplateHandler handler,
            ITenantContext ctx) =>
        {
            if (!Enum.TryParse<NotificationChannel>(req.Channel, true, out var channel))
                return (IResult)Results.Problem("Invalid channel.", statusCode: 400);
            if (!Enum.TryParse<NotificationCategory>(req.Category, true, out var category))
                return (IResult)Results.Problem("Invalid category.", statusCode: 400);

            var result = await handler.HandleCreateAsync(new CreateTemplateCommand(
                req.Name, category, channel,
                req.BodyPlain, req.SubjectTemplate, req.BodyHtmlTemplate));

            return result.IsSuccess
                ? (IResult)Results.Created($"/notification-templates/{result.Value}", new CreatedResponse(result.Value))
                : result.ToHttpResult();
        });

        templates.MapPut("/{id:guid}", async (
            Guid id,
            UpdateTemplateRequest req,
            TemplateHandler handler) =>
        {
            var result = await handler.HandleUpdateAsync(new UpdateTemplateCommand(
                id, req.BodyPlain, req.SubjectTemplate, req.BodyHtmlTemplate));
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        });

        templates.MapPost("/{id:guid}/activate", async (
            Guid id,
            TemplateHandler handler) =>
        {
            var result = await handler.HandleActivateAsync(id);
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        });

        // ─── Preferences ──────────────────────────────────────────────────────

        preferences.MapGet("/", async (
            NotificationDbContext db,
            ITenantContext ctx,
            HttpContext http) =>
        {
            var userId = GetCurrentUserId(http);
            if (userId == Guid.Empty) return (IResult)Results.Unauthorized();

            var prefs = await db.Preferences
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .ToListAsync();

            return Results.Ok(prefs.Select(p =>
                new PreferenceResponse(p.Id, p.Channel.ToString(), p.Category.ToString(), p.IsEnabled)));
        });

        preferences.MapPut("/", async (
            UpsertPreferencesRequest req,
            PreferenceHandler handler,
            HttpContext http) =>
        {
            var userId = GetCurrentUserId(http);
            if (userId == Guid.Empty) return (IResult)Results.Unauthorized();

            var items = req.Preferences
                .Select(p =>
                {
                    if (!Enum.TryParse<NotificationChannel>(p.Channel, true, out var ch))
                        throw new ArgumentException($"Invalid channel: {p.Channel}");
                    if (!Enum.TryParse<NotificationCategory>(p.Category, true, out var cat))
                        throw new ArgumentException($"Invalid category: {p.Category}");
                    return new UpsertPreferenceItem(ch, cat, p.IsEnabled);
                })
                .ToList();

            var result = await handler.HandleUpsertAsync(userId, items);
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        });

        // ─── In-App Inbox ─────────────────────────────────────────────────────

        inbox.MapGet("/", async (
            NotificationDbContext db,
            HttpContext http,
            int page     = 1,
            int pageSize = 25) =>
        {
            pageSize = Math.Min(pageSize, 100);
            var userId = GetCurrentUserId(http);
            if (userId == Guid.Empty) return (IResult)Results.Unauthorized();

            var query = db.InAppItems
                .Where(n => n.RecipientUserId == userId)
                .OrderBy(n => n.IsRead)
                .ThenByDescending(n => n.CreatedAt);

            var total = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync();

            return Results.Ok(new PagedInAppResponse(
                items.Select(ToInAppResponse).ToList(), total, page, pageSize));
        });

        inbox.MapGet("/unread-count", async (
            NotificationDbContext db,
            HttpContext http) =>
        {
            var userId = GetCurrentUserId(http);
            if (userId == Guid.Empty) return (IResult)Results.Unauthorized();

            var count = await db.InAppItems
                .CountAsync(n => n.RecipientUserId == userId && !n.IsRead);

            return Results.Ok(new UnreadCountResponse(count));
        });

        inbox.MapPost("/{id:guid}/read", async (
            Guid id,
            NotificationDbContext db,
            HttpContext http) =>
        {
            var userId = GetCurrentUserId(http);
            if (userId == Guid.Empty) return (IResult)Results.Unauthorized();

            var item = await db.InAppItems
                .FirstOrDefaultAsync(n => n.Id == id && n.RecipientUserId == userId);

            if (item is null) return (IResult)Results.NotFound();

            item.MarkRead();
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        inbox.MapPost("/read-all", async (
            NotificationDbContext db,
            HttpContext http) =>
        {
            var userId = GetCurrentUserId(http);
            if (userId == Guid.Empty) return (IResult)Results.Unauthorized();

            await db.InAppItems
                .Where(n => n.RecipientUserId == userId && !n.IsRead)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.IsRead,    true)
                    .SetProperty(n => n.ReadAt,    DateTime.UtcNow)
                    .SetProperty(n => n.UpdatedAt, DateTime.UtcNow));

            return Results.NoContent();
        });

        // ─── Internal send (called by other services) ─────────────────────────

        internalGrp.MapPost("/send", async (
            SendNotificationRequest req,
            SendNotificationHandler handler) =>
        {
            if (!Enum.TryParse<NotificationChannel>(req.Channel, true, out var channel))
                return (IResult)Results.Problem("Invalid channel.", statusCode: 400);
            if (!Enum.TryParse<NotificationCategory>(req.Category, true, out var category))
                return (IResult)Results.Problem("Invalid category.", statusCode: 400);

            var result = await handler.HandleAsync(new SendNotificationCommand(
                req.TenantId,
                req.RecipientUserId,
                req.RecipientAddress,
                channel,
                category,
                req.BodyPlain,
                req.Subject,
                req.BodyHtml,
                req.TemplateId,
                req.Variables));

            return result.IsSuccess
                ? (IResult)Results.Ok(new CreatedResponse(result.Value!))
                : result.ToHttpResult();
        });

        // ─── ACS Webhook ──────────────────────────────────────────────────────

        webhooks.MapPost("/acs", async (
            HttpContext http,
            NotificationDbContext db,
            IOptions<AcsOptions> acsOpts) =>
        {
            // Validate HMAC signature
            if (!await ValidateAcsHmacAsync(http.Request, acsOpts.Value.AcsWebhookHmacSecret))
                return (IResult)Results.Unauthorized();

            var events = await http.Request.ReadFromJsonAsync<List<AcsWebhookEvent>>();
            if (events is null) return (IResult)Results.BadRequest();

            foreach (var evt in events)
            {
                if (string.IsNullOrWhiteSpace(evt.MessageId)) continue;

                var record = await db.Records
                    .FirstOrDefaultAsync(r => r.ProviderMessageId == evt.MessageId);

                if (record is null) continue;

                var webhookEvent = evt.EventType.ToLowerInvariant() switch
                {
                    "deliveryreportreceived" when evt.DeliveryStatus == "Delivered" => (DeliveryWebhookEvent?)DeliveryWebhookEvent.Delivered,
                    "deliveryreportreceived" when evt.DeliveryStatus == "Failed"    => DeliveryWebhookEvent.Failed,
                    "deliveryreportreceived" when evt.DeliveryStatus == "Bounced"   => DeliveryWebhookEvent.Bounced,
                    "emailopened"    => DeliveryWebhookEvent.Opened,
                    "emaillinkclicked" => DeliveryWebhookEvent.Clicked,
                    _ => null
                };

                if (webhookEvent.HasValue)
                {
                    record.UpdateStatus(webhookEvent.Value);
                }
            }

            await db.SaveChangesAsync();
            return Results.Ok();
        });

        return app;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static Guid GetCurrentUserId(HttpContext http)
    {
        var claim = http.User.FindFirst("sub") ?? http.User.FindFirst("oid");
        return claim is not null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
    }

    private static async Task<bool> ValidateAcsHmacAsync(HttpRequest req, string secret)
    {
        if (!req.Headers.TryGetValue("x-acs-signature", out var sigHeader))
            return false;

        req.EnableBuffering();
        using var reader = new StreamReader(req.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        req.Body.Position = 0;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed   = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
        return string.Equals(computed, sigHeader.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static TemplateResponse ToTemplateResponse(Domain.Entities.NotificationTemplate t) => new(
        t.Id, t.Name, t.Category.ToString(), t.Channel.ToString(),
        t.SubjectTemplate, t.BodyHtmlTemplate, t.BodyPlainTemplate,
        t.IsActive, t.Version, t.CreatedAt, t.UpdatedAt);

    private static InAppNotificationResponse ToInAppResponse(Domain.Entities.InAppNotification n) => new(
        n.Id, n.Title, n.Body, n.ActionUrl,
        n.Category.ToString(), n.IsRead, n.ReadAt, n.CreatedAt);
}
