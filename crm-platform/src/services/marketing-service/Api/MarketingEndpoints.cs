using CrmPlatform.MarketingService.Api.Dtos;
using CrmPlatform.MarketingService.Application.Campaigns;
using CrmPlatform.MarketingService.Application.Journeys;
using CrmPlatform.MarketingService.Application.Templates;
using CrmPlatform.MarketingService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.MarketingService.Api;

public static class MarketingEndpoints
{
    public static IEndpointRouteBuilder MapMarketingEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Campaigns ────────────────────────────────────────────────────────

        app.MapGet("/campaigns", async (
            [FromQuery] int page,
            [FromQuery] int pageSize,
            MarketingDbContext db,
            CancellationToken ct) =>
        {
            page     = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = db.Campaigns.OrderByDescending(c => c.CreatedAt);
            var total = await query.CountAsync(ct);
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new CampaignSummaryResponse(
                    c.Id, c.Name, c.Channel, c.Status,
                    c.ScheduledAt, c.StartedAt, c.EndedAt, c.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(new { total, page, pageSize, items });
        }).RequireAuthorization();

        app.MapGet("/campaigns/{id:guid}", async (
            Guid id,
            MarketingDbContext db,
            CancellationToken ct) =>
        {
            var c = await db.Campaigns
                .Where(x => x.Id == id)
                .Select(x => new CampaignSummaryResponse(
                    x.Id, x.Name, x.Channel, x.Status,
                    x.ScheduledAt, x.StartedAt, x.EndedAt, x.CreatedAt))
                .FirstOrDefaultAsync(ct);

            return c is null ? Results.NotFound() : Results.Ok(c);
        }).RequireAuthorization();

        app.MapPost("/campaigns", async (
            [FromBody] CreateCampaignRequest req,
            CreateCampaignHandler handler,
            ITenantContext ctx,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new CreateCampaignCommand(req.Name, req.Description, req.Channel, ctx.UserId),
                ct);

            return result.IsSuccess
                ? Results.Created($"/campaigns/{result.Value!.CampaignId}",
                    new CreateCampaignResponse(result.Value.CampaignId, result.Value.Name))
                : result.ToHttpResult();
        }).RequireAuthorization();

        app.MapPost("/campaigns/{id:guid}/transition", async (
            Guid id,
            [FromBody] TransitionCampaignRequest req,
            TransitionCampaignHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new TransitionCampaignCommand(id, req.Action, req.ScheduledAt),
                ct);

            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        }).RequireAuthorization();

        // ── Journeys ─────────────────────────────────────────────────────────

        app.MapGet("/campaigns/{campaignId:guid}/journeys", async (
            Guid campaignId,
            MarketingDbContext db,
            CancellationToken ct) =>
        {
            var items = await db.Journeys
                .Where(j => j.CampaignId == campaignId)
                .OrderByDescending(j => j.CreatedAt)
                .Select(j => new JourneySummaryResponse(
                    j.Id, j.Name, j.IsPublished, j.StepCount, j.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(items);
        }).RequireAuthorization();

        app.MapPost("/journeys", async (
            [FromBody] CreateJourneyRequest req,
            CreateJourneyHandler handler,
            ITenantContext ctx,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new CreateJourneyCommand(req.CampaignId, req.Name, req.Description, ctx.UserId),
                ct);

            return result.IsSuccess
                ? Results.Created($"/journeys/{result.Value!.JourneyId}",
                    new CreateJourneyResponse(result.Value.JourneyId, result.Value.Name))
                : result.ToHttpResult();
        }).RequireAuthorization();

        app.MapPut("/journeys/{id:guid}/steps", async (
            Guid id,
            [FromBody] SetJourneyStepsRequest req,
            PublishJourneyHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleSetStepsAsync(
                new SetJourneyStepsCommand(id, req.StepsJson, req.StepCount),
                ct);

            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        }).RequireAuthorization();

        app.MapPost("/journeys/{id:guid}/publish", async (
            Guid id,
            PublishJourneyHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandlePublishAsync(new PublishJourneyCommand(id), ct);
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        }).RequireAuthorization();

        app.MapPost("/journeys/{id:guid}/enrollments", async (
            Guid id,
            [FromBody] EnrollContactRequest req,
            EnrollContactHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new EnrollContactCommand(id, req.ContactId), ct);

            return result.IsSuccess
                ? Results.Created($"/enrollments/{result.Value!.EnrollmentId}",
                    new EnrollContactResponse(result.Value.EnrollmentId))
                : result.ToHttpResult();
        }).RequireAuthorization();

        // ── Email Templates ──────────────────────────────────────────────────

        app.MapGet("/templates", async (
            [FromQuery] int page,
            [FromQuery] int pageSize,
            MarketingDbContext db,
            CancellationToken ct) =>
        {
            page     = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = db.EmailTemplates.OrderByDescending(t => t.CreatedAt);
            var total = await query.CountAsync(ct);
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new EmailTemplateSummaryResponse(
                    t.Id, t.Name, t.Subject, t.Engine,
                    t.Version, t.IsPublished, t.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(new { total, page, pageSize, items });
        }).RequireAuthorization();

        app.MapPost("/templates", async (
            [FromBody] CreateEmailTemplateRequest req,
            CreateEmailTemplateHandler handler,
            ITenantContext ctx,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new CreateEmailTemplateCommand(
                    req.Name, req.Subject, req.HtmlBody,
                    req.PlainTextBody, req.Engine, ctx.UserId),
                ct);

            return result.IsSuccess
                ? Results.Created($"/templates/{result.Value!.TemplateId}",
                    new CreateEmailTemplateResponse(result.Value.TemplateId, result.Value.Name))
                : result.ToHttpResult();
        }).RequireAuthorization();

        app.MapPost("/templates/{id:guid}/publish", async (
            Guid id,
            MarketingDbContext db,
            CancellationToken ct) =>
        {
            var template = await db.EmailTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (template is null) return Results.NotFound();

            try { template.Publish(); }
            catch (InvalidOperationException ex)
            {
                return Results.UnprocessableEntity(new { detail = ex.Message });
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();

        return app;
    }
}
