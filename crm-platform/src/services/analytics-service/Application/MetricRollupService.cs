using CrmPlatform.AnalyticsService.Domain.Entities;
using CrmPlatform.AnalyticsService.Domain.Enums;
using CrmPlatform.AnalyticsService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrmPlatform.AnalyticsService.Application;

/// <summary>
/// Runs every hour. Materialises daily MetricSnapshot rows from the raw AnalyticsEvent log.
/// Processes the previous complete hour bucket to avoid partial data.
/// Upserts snapshots — idempotent on re-run.
/// </summary>
public sealed class MetricRollupService(
    IServiceScopeFactory scopeFactory,
    ILogger<MetricRollupService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    // Map from EventType string to the MetricKey it increments
    private static readonly IReadOnlyDictionary<string, MetricKey> EventToMetric =
        new Dictionary<string, MetricKey>(StringComparer.OrdinalIgnoreCase)
        {
            ["lead.created"]          = MetricKey.LeadsCreated,
            ["lead.converted"]        = MetricKey.LeadsConverted,
            ["opportunity.won"]       = MetricKey.OpportunitiesWon,
            ["opportunity.lost"]      = MetricKey.OpportunitiesLost,
            ["case.created"]          = MetricKey.CasesCreated,
            ["case.resolved"]         = MetricKey.CasesResolved,
            ["case.closed"]           = MetricKey.CasesClosed,
            ["sla.breached"]          = MetricKey.SlaBreaches,
            ["journey.completed"]     = MetricKey.JourneysCompleted,
            ["campaign.activated"]    = MetricKey.CampaignsActivated,
            ["journey.enrollment.created"] = MetricKey.EnrollmentsCreated,
            ["tenant.provisioned"]    = MetricKey.TenantsProvisioned
        };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MetricRollupService started — interval {Interval}", Interval);

        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RollupAsync(stoppingToken);
        }
    }

    private async Task RollupAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db   = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
            var now  = DateTime.UtcNow;

            // Process events from the previous complete hour bucket
            var bucketEnd   = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
            var bucketStart = bucketEnd.AddHours(-1);
            var dayBucket   = bucketEnd.Date; // daily rollup aligns to midnight UTC

            var events = await db.Events
                .IgnoreQueryFilters()
                .Where(e =>
                    e.OccurredAt >= bucketStart
                    && e.OccurredAt < bucketEnd
                    && !e.IsDeleted)
                .ToListAsync(ct);

            if (events.Count == 0)
            {
                logger.LogInformation(
                    "MetricRollupService: no events in bucket {Start} – {End}",
                    bucketStart, bucketEnd);
                return;
            }

            // Group by tenant + event type and count
            var groups = events
                .Where(e => EventToMetric.ContainsKey(e.EventType))
                .GroupBy(e => new { e.TenantId, e.EventType });

            foreach (var group in groups)
            {
                var metricKey = EventToMetric[group.Key.EventType];
                var count     = (decimal)group.Count();

                // ── Daily snapshot ────────────────────────────────────────────
                await UpsertSnapshotAsync(
                    db, group.Key.TenantId, metricKey,
                    MetricGranularity.Daily, dayBucket, count, ct);

                logger.LogInformation(
                    "MetricRollup: tenant {TenantId} key {Key} += {Count} for bucket {Day}",
                    group.Key.TenantId, metricKey, count, dayBucket);
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "MetricRollupService encountered an error during rollup");
        }
    }

    private static async Task UpsertSnapshotAsync(
        AnalyticsDbContext db,
        Guid              tenantId,
        MetricKey         key,
        MetricGranularity granularity,
        DateTime          bucketStart,
        decimal           delta,
        CancellationToken ct)
    {
        var existing = await db.MetricSnapshots
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s =>
                s.TenantId    == tenantId
                && s.Key        == key
                && s.Granularity == granularity
                && s.BucketStart == bucketStart,
                ct);

        if (existing is not null)
        {
            existing.Accumulate(delta);
        }
        else
        {
            db.MetricSnapshots.Add(
                MetricSnapshot.Create(tenantId, key, granularity, bucketStart, delta));
        }
    }
}
