using CrmPlatform.AnalyticsService.Domain.Entities;
using CrmPlatform.AnalyticsService.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace CrmPlatform.AnalyticsService.Tests.Domain;

// ── AnalyticsEvent ────────────────────────────────────────────────────────────

public sealed class AnalyticsEventTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Record_SetsAllProperties()
    {
        var occurredAt = new DateTime(2026, 4, 12, 9, 0, 0, DateTimeKind.Utc);

        var evt = AnalyticsEvent.Record(
            TenantId,
            EventSource.Sfa,
            "lead.created",
            "entity-abc",
            """{"key":"value"}""",
            occurredAt);

        evt.TenantId.Should().Be(TenantId);
        evt.Source.Should().Be(EventSource.Sfa);
        evt.EventType.Should().Be("lead.created");
        evt.SourceId.Should().Be("entity-abc");
        evt.PayloadJson.Should().Be("""{"key":"value"}""");
        evt.OccurredAt.Should().Be(occurredAt);
        evt.Id.Should().NotBeEmpty();
        evt.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Record_EmptySourceId_IsAllowed()
    {
        // SourceId is optional — some events are not tied to a specific entity
        var evt = AnalyticsEvent.Record(
            TenantId,
            EventSource.Platform,
            "tenant.provisioned",
            string.Empty,
            "{}",
            DateTime.UtcNow);

        evt.SourceId.Should().BeEmpty();
    }

    [Fact]
    public void Record_BlankEventType_Throws()
    {
        var act = () => AnalyticsEvent.Record(
            TenantId,
            EventSource.Css,
            "   ",                  // whitespace-only
            "id-1",
            "{}",
            DateTime.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Record_BlankPayloadJson_Throws()
    {
        var act = () => AnalyticsEvent.Record(
            TenantId,
            EventSource.Marketing,
            "campaign.activated",
            "id-1",
            "",                     // empty payload
            DateTime.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Record_EachCallProducesUniqueId()
    {
        var evt1 = AnalyticsEvent.Record(TenantId, EventSource.Sfa, "lead.created", "a", "{}", DateTime.UtcNow);
        var evt2 = AnalyticsEvent.Record(TenantId, EventSource.Sfa, "lead.created", "b", "{}", DateTime.UtcNow);

        evt1.Id.Should().NotBe(evt2.Id);
    }

    [Theory]
    [InlineData(EventSource.Sfa)]
    [InlineData(EventSource.Css)]
    [InlineData(EventSource.Marketing)]
    [InlineData(EventSource.Identity)]
    [InlineData(EventSource.Platform)]
    public void Record_AcceptsAllEventSources(EventSource source)
    {
        var act = () => AnalyticsEvent.Record(
            TenantId, source, "any.event", "id", "{}", DateTime.UtcNow);

        act.Should().NotThrow();
    }
}

// ── MetricSnapshot ────────────────────────────────────────────────────────────

public sealed class MetricSnapshotTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Bucket =
        new(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_SetsAllProperties()
    {
        var snap = MetricSnapshot.Create(
            TenantId,
            MetricKey.LeadsCreated,
            MetricGranularity.Daily,
            Bucket,
            42m);

        snap.TenantId.Should().Be(TenantId);
        snap.Key.Should().Be(MetricKey.LeadsCreated);
        snap.Granularity.Should().Be(MetricGranularity.Daily);
        snap.BucketStart.Should().Be(Bucket);
        snap.Value.Should().Be(42m);
        snap.Id.Should().NotBeEmpty();
        snap.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Accumulate_AddsToValue()
    {
        var snap = MetricSnapshot.Create(
            TenantId, MetricKey.CasesCreated, MetricGranularity.Daily, Bucket, 10m);

        snap.Accumulate(5m);

        snap.Value.Should().Be(15m);
    }

    [Fact]
    public void Accumulate_MultipleDeltas_SumCorrectly()
    {
        var snap = MetricSnapshot.Create(
            TenantId, MetricKey.RevenueWon, MetricGranularity.Daily, Bucket, 1000m);

        snap.Accumulate(250m);
        snap.Accumulate(750m);

        snap.Value.Should().Be(2000m);
    }

    [Fact]
    public void Accumulate_UpdatesUpdatedAt()
    {
        var snap = MetricSnapshot.Create(
            TenantId, MetricKey.OpportunitiesWon, MetricGranularity.Daily, Bucket, 1m);

        var beforeAccumulate = snap.UpdatedAt;
        snap.Accumulate(1m);

        snap.UpdatedAt.Should().BeOnOrAfter(beforeAccumulate);
    }

    [Fact]
    public void Create_ZeroValue_IsValid()
    {
        // Rollup may legitimately produce a zero snapshot for a quiet period
        var snap = MetricSnapshot.Create(
            TenantId, MetricKey.SlaBreaches, MetricGranularity.Daily, Bucket, 0m);

        snap.Value.Should().Be(0m);
    }

    [Fact]
    public void Create_EachCallProducesUniqueId()
    {
        var snap1 = MetricSnapshot.Create(TenantId, MetricKey.LeadsCreated, MetricGranularity.Daily, Bucket, 1m);
        var snap2 = MetricSnapshot.Create(TenantId, MetricKey.LeadsCreated, MetricGranularity.Daily, Bucket, 1m);

        snap1.Id.Should().NotBe(snap2.Id);
    }

    [Theory]
    [InlineData(MetricKey.LeadsCreated)]
    [InlineData(MetricKey.LeadsConverted)]
    [InlineData(MetricKey.OpportunitiesWon)]
    [InlineData(MetricKey.OpportunitiesLost)]
    [InlineData(MetricKey.RevenueWon)]
    [InlineData(MetricKey.CasesCreated)]
    [InlineData(MetricKey.CasesResolved)]
    [InlineData(MetricKey.CasesClosed)]
    [InlineData(MetricKey.SlaBreaches)]
    [InlineData(MetricKey.MeanTimeToResolve)]
    [InlineData(MetricKey.JourneysCompleted)]
    [InlineData(MetricKey.CampaignsActivated)]
    [InlineData(MetricKey.EnrollmentsCreated)]
    [InlineData(MetricKey.TenantsProvisioned)]
    public void Create_AcceptsAllMetricKeys(MetricKey key)
    {
        var act = () => MetricSnapshot.Create(
            TenantId, key, MetricGranularity.Daily, Bucket, 1m);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(MetricGranularity.Hourly)]
    [InlineData(MetricGranularity.Daily)]
    [InlineData(MetricGranularity.Weekly)]
    [InlineData(MetricGranularity.Monthly)]
    public void Create_AcceptsAllGranularities(MetricGranularity granularity)
    {
        var act = () => MetricSnapshot.Create(
            TenantId, MetricKey.LeadsCreated, granularity, Bucket, 1m);

        act.Should().NotThrow();
    }
}
