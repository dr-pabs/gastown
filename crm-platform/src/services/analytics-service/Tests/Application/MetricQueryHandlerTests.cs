using CrmPlatform.AnalyticsService.Application;
using CrmPlatform.AnalyticsService.Domain.Entities;
using CrmPlatform.AnalyticsService.Domain.Enums;
using CrmPlatform.AnalyticsService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace CrmPlatform.AnalyticsService.Tests.Application;

public sealed class MetricQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static AnalyticsDbContext CreateContext(string dbName)
    {
        var opts = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(dbName).Options;
        var accessor = new Mock<ITenantContextAccessor>();
        accessor.Setup(a => a.TenantId).Returns(TenantId);
        return new AnalyticsDbContext(opts, accessor.Object);
    }

    private static Mock<ITenantContext> MakeCtx()
    {
        var m = new Mock<ITenantContext>();
        m.Setup(c => c.TenantId).Returns(TenantId);
        return m;
    }

    // ── GetTimeSeries ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTimeSeries_ReturnsPointsInRange()
    {
        using var db = CreateContext("ts-happy-" + Guid.NewGuid());

        var day1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var day3 = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        db.MetricSnapshots.AddRange(
            MetricSnapshot.Create(TenantId, MetricKey.LeadsCreated, MetricGranularity.Daily, day1, 5),
            MetricSnapshot.Create(TenantId, MetricKey.LeadsCreated, MetricGranularity.Daily, day2, 8),
            MetricSnapshot.Create(TenantId, MetricKey.LeadsCreated, MetricGranularity.Daily, day3, 3));
        await db.SaveChangesAsync();

        var handler = new MetricQueryHandler(db, MakeCtx().Object);
        var result = await handler.GetTimeSeriesAsync(
            new MetricTimeSeriesQuery(
                MetricKey.LeadsCreated,
                MetricGranularity.Daily,
                day1,
                day3)); // exclusive upper bound

        result.IsSuccess.Should().BeTrue();
        result.Value!.Points.Should().HaveCount(2);
        result.Value.Points[0].Value.Should().Be(5);
        result.Value.Points[1].Value.Should().Be(8);
    }

    [Fact]
    public async Task GetTimeSeries_FromAfterTo_ReturnsValidationError()
    {
        using var db = CreateContext("ts-invalid-" + Guid.NewGuid());
        var handler = new MetricQueryHandler(db, MakeCtx().Object);

        var result = await handler.GetTimeSeriesAsync(
            new MetricTimeSeriesQuery(
                MetricKey.LeadsCreated, MetricGranularity.Daily,
                DateTime.UtcNow.AddDays(1), DateTime.UtcNow));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(CrmPlatform.ServiceTemplate.Domain.ResultErrorCode.ValidationError);
    }

    // ── GetDashboardSummary ───────────────────────────────────────────────────

    [Fact]
    public async Task GetDashboardSummary_SumsAllKeys()
    {
        using var db = CreateContext("dash-" + Guid.NewGuid());

        var from = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var to   = new DateTime(2026, 4, 8, 0, 0, 0, DateTimeKind.Utc);

        db.MetricSnapshots.AddRange(
            MetricSnapshot.Create(TenantId, MetricKey.LeadsCreated,     MetricGranularity.Daily, from,           10),
            MetricSnapshot.Create(TenantId, MetricKey.LeadsCreated,     MetricGranularity.Daily, from.AddDays(1), 5),
            MetricSnapshot.Create(TenantId, MetricKey.OpportunitiesWon, MetricGranularity.Daily, from,            3),
            MetricSnapshot.Create(TenantId, MetricKey.CasesCreated,     MetricGranularity.Daily, from,            7),
            MetricSnapshot.Create(TenantId, MetricKey.SlaBreaches,      MetricGranularity.Daily, from,            2));
        await db.SaveChangesAsync();

        var handler = new MetricQueryHandler(db, MakeCtx().Object);
        var result  = await handler.GetDashboardSummaryAsync(from, to);

        result.IsSuccess.Should().BeTrue();
        result.Value!.LeadsCreated.Should().Be(15);
        result.Value.OpportunitiesWon.Should().Be(3);
        result.Value.CasesCreated.Should().Be(7);
        result.Value.SlaBreaches.Should().Be(2);
    }

    [Fact]
    public async Task GetDashboardSummary_EmptyPeriod_ReturnsZeros()
    {
        using var db = CreateContext("dash-empty-" + Guid.NewGuid());

        var handler = new MetricQueryHandler(db, MakeCtx().Object);
        var result  = await handler.GetDashboardSummaryAsync(
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        result.IsSuccess.Should().BeTrue();
        result.Value!.LeadsCreated.Should().Be(0);
        result.Value.SlaBreaches.Should().Be(0);
    }

    // ── MetricSnapshot domain ─────────────────────────────────────────────────

    [Fact]
    public void MetricSnapshot_Accumulate_AddsValue()
    {
        var snap = MetricSnapshot.Create(
            TenantId, MetricKey.CasesCreated, MetricGranularity.Daily,
            DateTime.UtcNow.Date, 10);

        snap.Accumulate(5);
        snap.Value.Should().Be(15);
    }
}
