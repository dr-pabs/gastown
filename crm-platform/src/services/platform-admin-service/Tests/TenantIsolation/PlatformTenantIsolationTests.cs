using CrmPlatform.PlatformAdminService.Domain.Entities;
using CrmPlatform.PlatformAdminService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.PlatformAdminService.Tests.TenantIsolation;

/// <summary>
/// Mandatory tenant isolation tests for platform-admin-service.
///
/// Platform admin operations bypass tenant query filters (IgnoreQueryFilters),
/// so these tests verify that the global EF filter IS present and DOES work
/// when IgnoreQueryFilters() is NOT applied.
///
/// This proves the filter exists and protects data in every non-admin context.
/// </summary>
[Trait("Category", "TenantIsolation")]
public sealed class PlatformTenantIsolationTests
{
    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = new("22222222-2222-2222-2222-222222222222");

    private static PlatformDbContext CreateContext(Guid activeTenantId, string dbName) =>
        new(
            new DbContextOptionsBuilder<PlatformDbContext>()
                .UseInMemoryDatabase(dbName).Options,
            new FixedTenantContextAccessor(activeTenantId));

    // ── Global EF filter is active without IgnoreQueryFilters ─────────────────

    [Fact]
    public async Task Tenants_WithoutIgnoreQueryFilters_OnlyReturnsTenantOwnedByContextTenant()
    {
        var sharedDb = "PlatformIsolation_Filter_" + Guid.NewGuid();

        // Seed: two tenants, each self-owned (TenantId == Id)
        using (var seed = CreateContext(TenantA, sharedDb))
        {
            var tA = BuildTenant(TenantA, "acme");
            var tB = BuildTenant(TenantB, "beta");
            seed.Tenants.Add(tA);
            seed.Tenants.Add(tB);
            await seed.SaveChangesAsync();
        }

        // Query with context = TenantA → should only see Tenant A (without IgnoreQueryFilters)
        using var ctxA = CreateContext(TenantA, sharedDb);
        var tenantsA = await ctxA.Tenants.ToListAsync();
        tenantsA.Should().ContainSingle(t => t.Slug == "acme",
            because: "EF global filter must restrict to TenantA's own rows");
        tenantsA.Should().NotContain(t => t.Slug == "beta",
            because: "TenantA must never see TenantB's tenant record without admin override");
    }

    [Fact]
    public async Task Tenants_WithIgnoreQueryFilters_ReturnsAllTenants()
    {
        var sharedDb = "PlatformIsolation_Admin_" + Guid.NewGuid();

        using (var seed = CreateContext(TenantA, sharedDb))
        {
            seed.Tenants.Add(BuildTenant(TenantA, "acme"));
            seed.Tenants.Add(BuildTenant(TenantB, "beta"));
            await seed.SaveChangesAsync();
        }

        // Platform admin bypasses the filter
        using var ctx = CreateContext(TenantA, sharedDb);
        var all = await ctx.Tenants.IgnoreQueryFilters().ToListAsync();
        all.Should().HaveCount(2, because: "admin context can see all tenants");
    }

    [Fact]
    public async Task SoftDeletedTenants_AreNotReturnedByDefaultFilter()
    {
        var sharedDb = "PlatformIsolation_SoftDelete_" + Guid.NewGuid();

        using (var seed = CreateContext(TenantA, sharedDb))
        {
            var t = BuildTenant(TenantA, "deleted-tenant");
            t.SoftDelete(); // mark deleted
            seed.Tenants.Add(t);
            await seed.SaveChangesAsync();
        }

        using var ctx = CreateContext(TenantA, sharedDb);
        var visible = await ctx.Tenants.ToListAsync();
        visible.Should().BeEmpty(because: "soft-deleted tenants are filtered by the global EF filter");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Tenant BuildTenant(Guid tenantId, string slug)
    {
        // Use reflection to set TenantId to a specific value
        // (mirrors how the platform-admin service wires TenantId == Id)
        var tenant = Tenant.Create($"Tenant-{slug}", slug, "plan", "test");

        typeof(CrmPlatform.ServiceTemplate.Domain.BaseEntity)
            .GetProperty("TenantId")!
            .SetValue(tenant, tenantId);

        // Override Id as well so tenant.Id == tenantId (self-owned)
        typeof(CrmPlatform.ServiceTemplate.Domain.BaseEntity)
            .GetProperty("Id")!
            .SetValue(tenant, tenantId);

        return tenant;
    }
}
