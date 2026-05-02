using CrmPlatform.IntegrationService.Domain.Entities;
using CrmPlatform.IntegrationService.Domain.Enums;
using CrmPlatform.IntegrationService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace CrmPlatform.IntegrationService.Tests.TenantIsolation;

/// <summary>
/// Verifies that global query filters on IntegrationDbContext prevent cross-tenant data leakage.
/// Uses a shared in-memory database name per test so multiple contexts can share state,
/// with per-tenant ITenantContextAccessor mocks controlling query filter scope.
/// </summary>
[Trait("Category", "TenantIsolation")]
public sealed class IntegrationTenantIsolationTests
{
    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = new("22222222-2222-2222-2222-222222222222");

    private static IntegrationDbContext CreateContext(string dbName, Guid tenantId)
    {
        var opts = new DbContextOptionsBuilder<IntegrationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var accessor = new Mock<ITenantContextAccessor>();
        accessor.Setup(a => a.TenantId).Returns(tenantId);
        return new IntegrationDbContext(opts, accessor.Object);
    }

    // ─── ConnectorConfig isolation ────────────────────────────────────────────

    [Fact]
    public async Task TenantBConnectors_NotVisibleTo_TenantA()
    {
        var db = Guid.NewGuid().ToString();
        using var ctxA = CreateContext(db, TenantA);
        using var ctxB = CreateContext(db, TenantB);

        ctxA.Connectors.Add(ConnectorConfig.Create(TenantA, ConnectorType.HubSpot,    "TenantA HubSpot"));
        ctxB.Connectors.Add(ConnectorConfig.Create(TenantB, ConnectorType.Salesforce, "TenantB Salesforce"));
        await ctxA.SaveChangesAsync();
        await ctxB.SaveChangesAsync();

        var tenantAConnectors = await ctxA.Connectors.ToListAsync();
        tenantAConnectors.Should().HaveCount(1);
        tenantAConnectors[0].Label.Should().Be("TenantA HubSpot");
        tenantAConnectors.Should().NotContain(c => c.TenantId == TenantB);
    }

    [Fact]
    public async Task TenantA_CanOnlySeeOwnConnectors_EvenWithSameType()
    {
        var db = Guid.NewGuid().ToString();
        using var ctxA = CreateContext(db, TenantA);
        using var ctxB = CreateContext(db, TenantB);

        ctxA.Connectors.Add(ConnectorConfig.Create(TenantA, ConnectorType.HubSpot, "TenantA HubSpot"));
        ctxB.Connectors.Add(ConnectorConfig.Create(TenantB, ConnectorType.HubSpot, "TenantB HubSpot"));
        await ctxA.SaveChangesAsync();
        await ctxB.SaveChangesAsync();

        var tenantAConnectors = await ctxA.Connectors.ToListAsync();
        tenantAConnectors.Should().HaveCount(1);
        tenantAConnectors[0].TenantId.Should().Be(TenantA);
    }

    // ─── OutboundJob isolation ────────────────────────────────────────────────

    [Fact]
    public async Task TenantBOutboundJobs_NotVisibleTo_TenantA()
    {
        var db          = Guid.NewGuid().ToString();
        var connectorId = Guid.NewGuid();

        using var ctxA = CreateContext(db, TenantA);
        using var ctxB = CreateContext(db, TenantB);

        ctxA.OutboundJobs.Add(
            OutboundJob.Create(TenantA, connectorId, ConnectorType.HubSpot, "lead.assigned", "{\"tid\":\"A\"}"));
        ctxB.OutboundJobs.Add(
            OutboundJob.Create(TenantB, connectorId, ConnectorType.HubSpot, "lead.assigned", "{\"tid\":\"B\"}"));
        await ctxA.SaveChangesAsync();
        await ctxB.SaveChangesAsync();

        var tenantAJobs = await ctxA.OutboundJobs.ToListAsync();
        tenantAJobs.Should().HaveCount(1);
        tenantAJobs[0].TenantId.Should().Be(TenantA);
    }

    // ─── InboundEvent isolation ───────────────────────────────────────────────

    [Fact]
    public async Task TenantBInboundEvents_NotVisibleTo_TenantA()
    {
        var db = Guid.NewGuid().ToString();
        using var ctxA = CreateContext(db, TenantA);
        using var ctxB = CreateContext(db, TenantB);

        ctxA.InboundEvents.Add(InboundEvent.Receive(TenantA, ConnectorType.HubSpot, "evt-A", "{\"tenantA\":true}"));
        ctxB.InboundEvents.Add(InboundEvent.Receive(TenantB, ConnectorType.HubSpot, "evt-B", "{\"tenantB\":true}"));
        await ctxA.SaveChangesAsync();
        await ctxB.SaveChangesAsync();

        var tenantAEvents = await ctxA.InboundEvents.ToListAsync();
        tenantAEvents.Should().HaveCount(1);
        tenantAEvents[0].ExternalEventId.Should().Be("evt-A");
        tenantAEvents.Should().NotContain(e => e.TenantId == TenantB);
    }

    // ─── Soft-delete filter ───────────────────────────────────────────────────

    [Fact]
    public async Task SoftDeleted_ConnectorConfig_NotReturnedByDefault()
    {
        var db = Guid.NewGuid().ToString();
        using var ctx = CreateContext(db, TenantA);

        var active  = ConnectorConfig.Create(TenantA, ConnectorType.HubSpot,    "Active");
        var deleted = ConnectorConfig.Create(TenantA, ConnectorType.Salesforce, "Deleted");
        deleted.IsDeleted  = true;
        deleted.DeletedAt  = DateTime.UtcNow;
        deleted.ModifiedAt = DateTime.UtcNow;

        ctx.Connectors.AddRange(active, deleted);
        await ctx.SaveChangesAsync();

        var visible = await ctx.Connectors.ToListAsync();
        visible.Should().HaveCount(1);
        visible[0].Label.Should().Be("Active");
    }

    // ─── Suspended connector dispatch guard ───────────────────────────────────

    [Fact]
    public async Task Suspended_Connector_IsNotReturnedByConnectedFilter()
    {
        var db = Guid.NewGuid().ToString();
        using var ctx = CreateContext(db, TenantA);

        var config = ConnectorConfig.Create(TenantA, ConnectorType.Salesforce, "SF");
        config.Connect("kv-secret", "org-id", null, null);
        ctx.Connectors.Add(config);
        await ctx.SaveChangesAsync();

        config.Suspend();
        await ctx.SaveChangesAsync();

        // Worker must NOT dispatch Suspended connectors
        var dispatchable = await ctx.Connectors
            .Where(c => c.Status == ConnectorStatus.Connected)
            .ToListAsync();

        dispatchable.Should().BeEmpty();
    }

    // ─── Platform admin IgnoreQueryFilters ────────────────────────────────────

    [Fact]
    public async Task PlatformAdmin_CanSeeAllTenantConnectors_ViaIgnoreQueryFilters()
    {
        var db = Guid.NewGuid().ToString();
        using var ctxA    = CreateContext(db, TenantA);
        using var ctxB    = CreateContext(db, TenantB);
        using var ctxAll  = CreateContext(db, Guid.Empty); // platform admin — no tenant filter needed

        ctxA.Connectors.Add(ConnectorConfig.Create(TenantA, ConnectorType.HubSpot,    "A HubSpot"));
        ctxB.Connectors.Add(ConnectorConfig.Create(TenantB, ConnectorType.Salesforce, "B Salesforce"));
        await ctxA.SaveChangesAsync();
        await ctxB.SaveChangesAsync();

        // Platform admin bypasses global filter with IgnoreQueryFilters
        var all = await ctxAll.Connectors.IgnoreQueryFilters().ToListAsync();
        all.Should().HaveCount(2);
        all.Select(c => c.TenantId).Should().Contain(TenantA);
        all.Select(c => c.TenantId).Should().Contain(TenantB);
    }
}
