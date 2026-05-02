using CrmPlatform.MarketingService.Domain.Entities;
using CrmPlatform.MarketingService.Domain.Enums;
using CrmPlatform.MarketingService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace CrmPlatform.MarketingService.Tests.TenantIsolation;

/// <summary>
/// Mandatory tenant isolation tests for the marketing service.
/// [Trait("Category","TenantIsolation")] — CI hard gate.
/// </summary>
[Trait("Category", "TenantIsolation")]
public sealed class MarketingTenantIsolationTests
{
    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid UserId  = Guid.NewGuid();

    private static MarketingDbContext CreateContext(string dbName, Guid tenantId)
    {
        var opts = new DbContextOptionsBuilder<MarketingDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var accessor = new Mock<ITenantContextAccessor>();
        accessor.Setup(a => a.TenantId).Returns(tenantId);
        return new MarketingDbContext(opts, accessor.Object);
    }

    private static Campaign MakeCampaign(Guid tenantId, string name)
        => Campaign.Create(tenantId, name, "desc", CampaignChannel.Email, UserId);

    // ─── Layer 1: tenant query filter ────────────────────────────────────────

    [Fact]
    public async Task Campaigns_TenantA_CannotSee_TenantB_Records()
    {
        var dbName = "MktIsolation_A_" + Guid.NewGuid();

        await using var seed = CreateContext(dbName, TenantA);
        seed.Campaigns.Add(MakeCampaign(TenantA, "A Campaign"));
        seed.Campaigns.Add(MakeCampaign(TenantB, "B Campaign"));
        await seed.SaveChangesAsync();

        await using var ctxA = CreateContext(dbName, TenantA);
        var campaigns = await ctxA.Campaigns.ToListAsync();

        campaigns.Should().OnlyContain(c => c.TenantId == TenantA);
        campaigns.Should().HaveCount(1);
    }

    [Fact]
    public async Task Campaigns_TenantB_CannotSee_TenantA_Records()
    {
        var dbName = "MktIsolation_B_" + Guid.NewGuid();

        await using var seed = CreateContext(dbName, TenantA);
        seed.Campaigns.Add(MakeCampaign(TenantA, "A Campaign"));
        seed.Campaigns.Add(MakeCampaign(TenantB, "B Campaign"));
        await seed.SaveChangesAsync();

        await using var ctxB = CreateContext(dbName, TenantB);
        var campaigns = await ctxB.Campaigns.ToListAsync();

        campaigns.Should().OnlyContain(c => c.TenantId == TenantB);
        campaigns.Should().HaveCount(1);
    }

    // ─── Layer 2: soft-delete filter ─────────────────────────────────────────

    [Fact]
    public async Task SoftDeletedCampaigns_NotReturnedByDefaultQuery()
    {
        var dbName = "MktIsolation_SoftDel_" + Guid.NewGuid();

        await using var seed = CreateContext(dbName, TenantA);
        var live    = MakeCampaign(TenantA, "Live Campaign");
        var deleted = MakeCampaign(TenantA, "Deleted Campaign");

        seed.Campaigns.Add(live);
        seed.Campaigns.Add(deleted);
        await seed.SaveChangesAsync();

        // Directly set IsDeleted without going through EF filter
        await seed.Campaigns
            .IgnoreQueryFilters()
            .Where(c => c.Id == deleted.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.IsDeleted, true)
                .SetProperty(c => c.DeletedAt, (DateTime?)DateTime.UtcNow));

        await using var ctx = CreateContext(dbName, TenantA);
        var visible = await ctx.Campaigns.ToListAsync();

        visible.Should().HaveCount(1);
        visible.Single().Id.Should().Be(live.Id);
    }

    // ─── Layer 3: enrollment isolation ───────────────────────────────────────

    [Fact]
    public async Task Enrollments_TenantA_CannotSee_TenantB_Enrollments()
    {
        var dbName     = "MktIsolation_Enroll_" + Guid.NewGuid();
        var journeyIdA = Guid.NewGuid();
        var journeyIdB = Guid.NewGuid();

        await using var seed = CreateContext(dbName, TenantA);
        seed.Enrollments.Add(JourneyEnrollment.Create(TenantA, journeyIdA, Guid.NewGuid()));
        seed.Enrollments.Add(JourneyEnrollment.Create(TenantB, journeyIdB, Guid.NewGuid()));
        await seed.SaveChangesAsync();

        await using var ctxA = CreateContext(dbName, TenantA);
        var enrollments = await ctxA.Enrollments.ToListAsync();

        enrollments.Should().OnlyContain(e => e.TenantId == TenantA);
        enrollments.Should().HaveCount(1);
    }
}
