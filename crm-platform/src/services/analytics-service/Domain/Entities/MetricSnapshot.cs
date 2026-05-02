using CrmPlatform.AnalyticsService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.AnalyticsService.Domain.Entities;

/// <summary>
/// Pre-aggregated metric snapshot for a given key, granularity, and time bucket.
/// Materialised by the MetricRollupService background service.
/// Supports the dashboard read-model queries.
/// </summary>
public sealed class MetricSnapshot : BaseEntity
{
    public MetricKey        Key         { get; private set; }
    public MetricGranularity Granularity { get; private set; }

    /// <summary>Start of the time bucket (e.g. 2026-04-12 00:00:00 UTC for a daily snapshot).</summary>
    public DateTime         BucketStart { get; private set; }

    /// <summary>Numeric value — count or sum depending on the MetricKey.</summary>
    public decimal          Value       { get; private set; }

    private MetricSnapshot() { } // EF Core

    public static MetricSnapshot Create(
        Guid             tenantId,
        MetricKey        key,
        MetricGranularity granularity,
        DateTime         bucketStart,
        decimal          value)
    {
        return new MetricSnapshot
        {
            Id          = Guid.NewGuid(),
            TenantId    = tenantId,
            Key         = key,
            Granularity = granularity,
            BucketStart = bucketStart,
            Value       = value
        };
    }

    /// <summary>Increment the snapshot value (used during rollup accumulation).</summary>
    public void Accumulate(decimal delta)
    {
        Value += delta;
    }
}
