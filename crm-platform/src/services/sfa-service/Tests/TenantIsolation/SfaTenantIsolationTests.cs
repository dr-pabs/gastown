using CrmPlatform.SfaService.Domain.Entities;
using CrmPlatform.SfaService.Domain.Enums;
using CrmPlatform.SfaService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CrmPlatform.SfaService.Tests.TenantIsolation;

/// <summary>
/// Mandatory tenant isolation tests for the SFA service.
/// Asserts that EF query filters prevent cross-tenant data leakage.
/// [Trait("Category", "TenantIsolation")] — runs in the 'test-tenant-isolation' Makefile target.
/// </summary>
[Trait("Category", "TenantIsolation")]
public sealed class SfaTenantIsolationTests
{
    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = new("22222222-2222-2222-2222-222222222222");

    private static SfaDbContext CreateContext(string dbName, Guid tenantId)
    {
        var opts = new DbContextOptionsBuilder<SfaDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var accessor = new Mock<ITenantContextAccessor>();
        accessor.Setup(a => a.TenantId).Returns(tenantId);
        return new SfaDbContext(opts, accessor.Object);
    }

    // ─── Leads ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Leads_TenantA_CannotSee_TenantB_Records()
    {
        var dbName = "SfaIsolation_Leads_" + Guid.NewGuid();

        // Seed both tenants using TenantA context (bypasses filter for seed because filter
        // is set on the accessor — TenantA context still writes TenantB's lead directly)
        await using var seedCtx = CreateContext(dbName, TenantA);
        var leadA = Lead.Create(TenantA, "Alice", "alice@a.com", null, null, LeadSource.Web, TenantA);
        var leadB = Lead.Create(TenantB, "Bob",   "bob@b.com",   null, null, LeadSource.Web, TenantB);
        seedCtx.Leads.Add(leadA);
        seedCtx.Leads.Add(leadB);
        await seedCtx.SaveChangesAsync();

        // Query as TenantA
        await using var ctxA = CreateContext(dbName, TenantA);
        var leads = await ctxA.Leads.ToListAsync();

        leads.Should().OnlyContain(l => l.TenantId == TenantA);
        leads.Should().HaveCount(1);
    }

    [Fact]
    public async Task Leads_TenantB_CannotSee_TenantA_Records()
    {
        var dbName = "SfaIsolation_LeadsB_" + Guid.NewGuid();

        await using var seedCtx = CreateContext(dbName, TenantA);
        var leadA = Lead.Create(TenantA, "Alice", "alice@a.com", null, null, LeadSource.Referral, TenantA);
        var leadB = Lead.Create(TenantB, "Bob",   "bob@b.com",   null, null, LeadSource.Referral, TenantB);
        seedCtx.Leads.Add(leadA);
        seedCtx.Leads.Add(leadB);
        await seedCtx.SaveChangesAsync();

        await using var ctxB = CreateContext(dbName, TenantB);
        var leads = await ctxB.Leads.ToListAsync();

        leads.Should().OnlyContain(l => l.TenantId == TenantB);
        leads.Should().HaveCount(1);
    }

    // ─── Opportunities ────────────────────────────────────────────────────────

    [Fact]
    public async Task Opportunities_TenantA_CannotSee_TenantB_Records()
    {
        var dbName = "SfaIsolation_Opps_" + Guid.NewGuid();

        await using var seedCtx = CreateContext(dbName, TenantA);
        var oppA = Opportunity.Create(TenantA, "Deal A", 1000m, null, null, null);
        var oppB = Opportunity.Create(TenantB, "Deal B", 2000m, null, null, null);
        seedCtx.Opportunities.Add(oppA);
        seedCtx.Opportunities.Add(oppB);
        await seedCtx.SaveChangesAsync();

        await using var ctxA = CreateContext(dbName, TenantA);
        var opps = await ctxA.Opportunities.ToListAsync();

        opps.Should().OnlyContain(o => o.TenantId == TenantA);
        opps.Should().HaveCount(1);
    }

    // ─── Soft-delete filter ───────────────────────────────────────────────────

    [Fact]
    public async Task SoftDeletedLeads_NotReturnedByDefaultQuery()
    {
        var dbName = "SfaIsolation_SoftDelete_" + Guid.NewGuid();

        await using var seedCtx = CreateContext(dbName, TenantA);
        var lead = Lead.Create(TenantA, "Deleted Lead", "del@a.com", null, null, LeadSource.Campaign, TenantA);
        seedCtx.Leads.Add(lead);
        await seedCtx.SaveChangesAsync();

        // Soft-delete via Disqualify
        lead.Disqualify();
        await seedCtx.SaveChangesAsync();

        await using var queryCtx = CreateContext(dbName, TenantA);
        var leads = await queryCtx.Leads.ToListAsync();
        leads.Should().BeEmpty();

        // IgnoreQueryFilters should reveal deleted record
        var allLeads = await queryCtx.Leads.IgnoreQueryFilters().ToListAsync();
        allLeads.Should().HaveCount(1);
    }
}
