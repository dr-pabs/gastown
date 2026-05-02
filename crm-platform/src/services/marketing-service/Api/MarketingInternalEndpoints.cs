using CrmPlatform.MarketingService.Application.Journeys;
using CrmPlatform.MarketingService.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrmPlatform.MarketingService.Api;

/// <summary>
/// Internal endpoints called only by the journey-orchestrator Durable Function.
/// Not exposed via API gateway — internal network only.
/// </summary>
public static class MarketingInternalEndpoints
{
    public static IEndpointRouteBuilder MapMarketingInternalEndpoints(this IEndpointRouteBuilder app)
    {
        var internal_ = app.MapGroup("/internal/enrollments");

        // ─── PATCH /internal/enrollments/{id}/instance-id ────────────────────
        // Called by StoreInstanceIdActivity — persists Durable Function instance ID.
        internal_.MapPatch("/{id:guid}/instance-id", async (
            Guid id,
            SetEnrollmentInstanceIdRequest req,
            MarketingDbContext db,
            ILogger<MarketingInternalEndpoints> logger) =>
        {
            var enrollment = await db.Enrollments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == req.TenantId);

            if (enrollment is null)
                return Results.NotFound();

            enrollment.SetDurableFunctionInstanceId(req.InstanceId);
            await db.SaveChangesAsync();

            logger.LogDebug(
                "Stored Durable instance {InstanceId} on enrollment {EnrollmentId}",
                req.InstanceId, id);

            return Results.NoContent();
        });

        // ─── POST /internal/enrollments/{id}/advance ─────────────────────────
        // Called by AdvanceEnrollmentActivity after each step completes.
        internal_.MapPost("/{id:guid}/advance", async (
            Guid id,
            EnrollmentTenantRequest req,
            MarketingDbContext db,
            ILogger<MarketingInternalEndpoints> logger) =>
        {
            var enrollment = await db.Enrollments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == req.TenantId);

            if (enrollment is null)
                return Results.NotFound();

            try
            {
                enrollment.AdvanceStep();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 409);
            }

            await db.SaveChangesAsync();

            logger.LogDebug(
                "Advanced enrollment {EnrollmentId} to step {Step}",
                id, enrollment.CurrentStep);

            return Results.NoContent();
        });

        // ─── POST /internal/enrollments/{id}/complete ─────────────────────────
        // Called by CompleteEnrollmentActivity when all journey steps are done.
        internal_.MapPost("/{id:guid}/complete", async (
            Guid id,
            EnrollmentTenantRequest req,
            CompleteEnrollmentHandler handler) =>
        {
            var result = await handler.HandleAsync(new CompleteEnrollmentCommand(id, req.TenantId));
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        });

        return app;
    }
}

public sealed record SetEnrollmentInstanceIdRequest(string InstanceId, Guid TenantId);
public sealed record EnrollmentTenantRequest(Guid TenantId);
