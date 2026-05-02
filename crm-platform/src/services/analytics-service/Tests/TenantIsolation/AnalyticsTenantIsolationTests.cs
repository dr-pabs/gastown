using CrmPlatform.AnalyticsService.Domain.Entities;
using CrmPlatform.AnalyticsService.Domain.Enums;
using CrmPlatform.AnalyticsService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace CrmPlatform.AnalyticsService.Tests.TenantIsolation;

/// <summary>
/// Mandatory tenant isolation tests for the analytics service.
/// [Trait("Category","TenantIsolation")] — CI hard gate.
/// </summary>
[Trait("Category", "TenantIsolation")]
public sealed class AnalyticsTenantIsolationTests
{
    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = new("22222222-2222-2222-2222-222222222222");

    private static AnalyticsDbContext CreateContext(string dbName, Guid tenantId)
    {
        var opts = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(dbName).Options;
        var accessor = new Mock<ITenantContextAccessor>();
        accessor.Setup(a => a.TenantId).Returns(tenantId);
        return new AnalyticsDbContext(opts, accessor.Object);
    }

    [Fact]
    public async Task Events_TenantA_CannotSee_TenantB_Events()
    {
        var dbName = "AnalyticsIsolation_A_" + Guid.NewGuid();

        await using var seed = CreateContext(dbName, TenantA);
        seed.Events.Add(AnalyticsEvent.Record(
            TenantA, EventSource.Sfa, "lead.created", "id-a", "{}", DateTime.UtcNow));
        seed.Events.Add(AnalyticsEvent.Record(
            TenantB, EventSource.Sfa, "lead.created", "id-b", "{}", DateTime.UtcNow));
        await seed.SaveChangesAsync();

        await using var ctxA = CreateContext(dbName, TenantA);
        var events = await ctxA.Events.ToListAsync();

        events.Should().OnlyContain(e => e.TenantId == TenantA);
        events.Should().HaveCount(1);
    }

    [Fact]
    public async Task Snapshots_TenantB_CannotSee_TenantA_Snapshots()
    {
        var dbName = "AnalyticsIsolation_B_" + Guid.NewGuid();
        var bucket = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        await using var seed = CreateContext(dbName, TenantA);
        seed.MetricSnapshots.Add(MetricSnapshot.Create(
            TenantA, MetricKey.LeadsCreated, MetricGranularity.Daily, bucket, 10));
        seed.MetricSnapshots.Add(MetricSnapshot.Create(
            TenantB, MetricKey.LeadsCreated, MetricGranularity.Daily, bucket, 20));
        await seed.SaveChangesAsync();

        await using var ctxB = CreateContext(dbName, TenantB);
        var snapshots = await ctxB.MetricSnapshots.ToListAsync();

        snapshots.Should().OnlyContain(s => s.TenantId == TenantB);
        snapshots.Should().HaveCount(1);
        snapshots.Single().Value.Should().Be(20);
    }

    [Fact]
    public async Task SoftDeletedEvents_NotReturnedByDefaultQuery()
    {
        var dbName = "AnalyticsIsolation_SoftDel_" + Guid.NewGuid();

        await using var seed = CreateContext(dbName, TenantA);
        var live    = AnalyticsEvent.Record(TenantA, EventSource.Css, "case.created", "id1", "{}", DateTime.UtcNow);
        var deleted = AnalyticsEvent.Record(TenantA, EventSource.Css, "case.created", "id2", "{}", DateTime.UtcNow);
        seed.Events.AddRange(live, deleted);
        await seed.SaveChangesAsync();

        await seed.Events
            .IgnoreQueryFilters()
            .Where(e => e.Id == deleted.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.IsDeleted, true)
                .SetProperty(e => e.DeletedAt, (DateTime?)DateTime.UtcNow));

        await using var ctx = CreateContext(dbName, TenantA);
        var visible = await ctx.Events.ToListAsync();

        visible.Should().HaveCount(1);
        visible.Single().Id.Should().Be(live.Id);
    }
}
