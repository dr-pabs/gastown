using CrmPlatform.AnalyticsService.Application;
using CrmPlatform.AnalyticsService.Domain.Enums;
using CrmPlatform.AnalyticsService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.AnalyticsService.Api;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Dashboard summary ─────────────────────────────────────────────────

        app.MapGet("/analytics/dashboard", async (
            [FromQuery] DateTime from,
            [FromQuery] DateTime to,
            MetricQueryHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.GetDashboardSummaryAsync(from, to, ct);
            return result.IsSuccess
                ? (IResult)Results.Ok(result.Value)
                : result.ToHttpResult();
        }).RequireAuthorization();

        // ── Time series for a single metric ───────────────────────────────────

        app.MapGet("/analytics/metrics/{key}", async (
            string key,
            [FromQuery] string granularity,
            [FromQuery] DateTime from,
            [FromQuery] DateTime to,
            MetricQueryHandler handler,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<MetricKey>(key, ignoreCase: true, out var metricKey))
                return (IResult)Results.BadRequest(new { detail = $"Unknown metric key '{key}'." });

            if (!Enum.TryParse<MetricGranularity>(granularity, ignoreCase: true, out var gran))
                return (IResult)Results.BadRequest(new { detail = $"Unknown granularity '{granularity}'." });

            var result = await handler.GetTimeSeriesAsync(
                new MetricTimeSeriesQuery(metricKey, gran, from, to), ct);

            return result.IsSuccess
                ? (IResult)Results.Ok(result.Value)
                : result.ToHttpResult();
        }).RequireAuthorization();

        // ── Raw event log (paginated, staff only) ─────────────────────────────

        app.MapGet("/analytics/events", async (
            [FromQuery] string? eventType,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            AnalyticsDbContext db,
            CancellationToken ct) =>
        {
            page     = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = db.Events.AsQueryable();

            if (!string.IsNullOrWhiteSpace(eventType))
                query = query.Where(e => e.EventType == eventType);
            if (from.HasValue)
                query = query.Where(e => e.OccurredAt >= from.Value);
            if (to.HasValue)
                query = query.Where(e => e.OccurredAt < to.Value);

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(e => e.OccurredAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(e => new
                {
                    e.Id, e.TenantId, e.Source, e.EventType,
                    e.SourceId, e.OccurredAt
                })
                .ToListAsync(ct);

            return Results.Ok(new { total, page, pageSize, items });
        }).RequireAuthorization();

        return app;
    }
}
