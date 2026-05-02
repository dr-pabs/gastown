using CrmPlatform.AnalyticsService.Domain.Enums;
using CrmPlatform.AnalyticsService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.AnalyticsService.Application;

// ── Query models ──────────────────────────────────────────────────────────────

public sealed record MetricTimeSeriesQuery(
    MetricKey         Key,
    MetricGranularity Granularity,
    DateTime          From,
    DateTime          To);

public sealed record MetricDataPoint(DateTime BucketStart, decimal Value);

public sealed record MetricTimeSeriesResult(
    MetricKey              Key,
    MetricGranularity      Granularity,
    IReadOnlyList<MetricDataPoint> Points);

public sealed record DashboardSummaryResult(
    decimal LeadsCreated,
    decimal LeadsConverted,
    decimal OpportunitiesWon,
    decimal RevenueWon,
    decimal CasesCreated,
    decimal CasesResolved,
    decimal SlaBreaches,
    decimal JourneysCompleted,
    DateTime From,
    DateTime To);

// ── Query handlers ────────────────────────────────────────────────────────────

public sealed class MetricQueryHandler(
    AnalyticsDbContext db,
    ITenantContext tenantContext)
{
    /// <summary>Returns a time-series for a single metric key.</summary>
    public async Task<Result<MetricTimeSeriesResult>> GetTimeSeriesAsync(
        MetricTimeSeriesQuery query,
        CancellationToken ct = default)
    {
        if (query.From >= query.To)
            return Result.Fail<MetricTimeSeriesResult>(
                "From must be before To.", ResultErrorCode.ValidationError);

        var points = await db.MetricSnapshots
            .Where(s =>
                s.Key         == query.Key
                && s.Granularity == query.Granularity
                && s.BucketStart >= query.From
                && s.BucketStart < query.To)
            .OrderBy(s => s.BucketStart)
            .Select(s => new MetricDataPoint(s.BucketStart, s.Value))
            .ToListAsync(ct);

        return Result.Ok(new MetricTimeSeriesResult(query.Key, query.Granularity, points));
    }

    /// <summary>
    /// Returns the headline dashboard summary for the current tenant
    /// summed over [from, to).
    /// </summary>
    public async Task<Result<DashboardSummaryResult>> GetDashboardSummaryAsync(
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        if (from >= to)
            return Result.Fail<DashboardSummaryResult>(
                "From must be before To.", ResultErrorCode.ValidationError);

        // Fetch all daily snapshots for the period in one query
        var snapshots = await db.MetricSnapshots
            .Where(s =>
                s.Granularity  == MetricGranularity.Daily
                && s.BucketStart >= from
                && s.BucketStart < to)
            .ToListAsync(ct);

        decimal Sum(MetricKey key)
            => snapshots.Where(s => s.Key == key).Sum(s => s.Value);

        return Result.Ok(new DashboardSummaryResult(
            LeadsCreated:      Sum(MetricKey.LeadsCreated),
            LeadsConverted:    Sum(MetricKey.LeadsConverted),
            OpportunitiesWon:  Sum(MetricKey.OpportunitiesWon),
            RevenueWon:        Sum(MetricKey.RevenueWon),
            CasesCreated:      Sum(MetricKey.CasesCreated),
            CasesResolved:     Sum(MetricKey.CasesResolved),
            SlaBreaches:       Sum(MetricKey.SlaBreaches),
            JourneysCompleted: Sum(MetricKey.JourneysCompleted),
            From: from,
            To:   to));
    }
}
