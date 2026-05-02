using CrmPlatform.CssService.Domain.Events;
using CrmPlatform.CssService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrmPlatform.CssService.Api;

/// <summary>
/// Internal endpoints called only by Durable Functions (sla-orchestrator).
/// Not exposed via API gateway — protected by network policy or internal API key.
/// </summary>
public static class CssInternalEndpoints
{
    public static IEndpointRouteBuilder MapCssInternalEndpoints(this IEndpointRouteBuilder app)
    {
        var internal_ = app.MapGroup("/internal/cases");
        // No RequireAuthorization — internal network only.
        // Add .RequireHost("localhost") or internal API key middleware in production.

        // ─── PATCH /internal/cases/{id}/instance-id ───────────────────────────
        // Called by sla-orchestrator to store the Durable Function InstanceId.
        internal_.MapPatch("/{id:guid}/instance-id", async (
            Guid id,
            SetInstanceIdRequest req,
            CssDbContext db,
            ILogger<CssEndpoints> logger) =>
        {
            var c = await db.Cases
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == req.TenantId);

            if (c is null)
                return Results.NotFound();

            c.SetDurableFunctionInstanceId(req.InstanceId);
            await db.SaveChangesAsync();

            logger.LogDebug(
                "Stored Durable Function instance ID {InstanceId} on case {CaseId}",
                req.InstanceId, id);

            return Results.NoContent();
        });

        // ─── POST /internal/cases/{id}/sla-warning ────────────────────────────
        // Called by sla-orchestrator at the 80% SLA duration threshold.
        internal_.MapPost("/{id:guid}/sla-warning", async (
            Guid id,
            TenantRequest req,
            CssDbContext db,
            IEventPublisher publisher,
            ILogger<CssEndpoints> logger) =>
        {
            var c = await db.Cases
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == req.TenantId);

            if (c is null)
                return Results.NotFound();

            // Already resolved/closed — SLA warning no longer relevant
            if (c.Status is Domain.Enums.CaseStatus.Resolved or Domain.Enums.CaseStatus.Closed)
            {
                logger.LogInformation(
                    "SLA warning ignored — case {CaseId} already in terminal state {Status}.",
                    id, c.Status);
                return Results.NoContent();
            }

            var evt = new SlaBreachedEvent(
                id, req.TenantId, "Warning", c.SlaDeadline!.Value, DateTime.UtcNow);

            await publisher.PublishAsync(evt);
            await db.SaveChangesAsync();

            logger.LogWarning("SLA warning (80%) raised for case {CaseId}.", id);
            return Results.NoContent();
        });

        // ─── POST /internal/cases/{id}/sla-breach ────────────────────────────
        // Called by sla-orchestrator when the SLA deadline has elapsed.
        internal_.MapPost("/{id:guid}/sla-breach", async (
            Guid id,
            TenantRequest req,
            CssDbContext db,
            IEventPublisher publisher,
            ILogger<CssEndpoints> logger) =>
        {
            var c = await db.Cases
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == req.TenantId);

            if (c is null)
                return Results.NotFound();

            // Idempotent — already breached
            if (c.SlaBreached)
            {
                logger.LogInformation("Case {CaseId} already marked SLA breached.", id);
                return Results.NoContent();
            }

            c.MarkSlaBreached();

            var evt = new SlaBreachedEvent(
                id, req.TenantId, "Breached", c.SlaDeadline!.Value, DateTime.UtcNow);

            await publisher.PublishAsync(evt);
            await db.SaveChangesAsync();

            logger.LogError("SLA BREACHED for case {CaseId}.", id);
            return Results.NoContent();
        });

        return app;
    }
}

public sealed record SetInstanceIdRequest(string InstanceId, Guid TenantId);
public sealed record TenantRequest(Guid TenantId);
