using CrmPlatform.IdentityService.Domain.Entities;
using CrmPlatform.IdentityService.Domain.Enums;
using CrmPlatform.IdentityService.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CrmPlatform.ServiceTemplate.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.IdentityService.Tests.TenantIsolation;

/// <summary>
/// Mandatory tenant isolation tests — every endpoint must pass these.
/// These tests assert that EF query filters prevent cross-tenant data leakage.
///
/// RULE from CLAUDE.md: Tenant isolation test mandatory on every endpoint.
/// </summary>
[Trait("Category", "TenantIsolation")]
public sealed class IdentityTenantIsolationTests
{
    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = new("22222222-2222-2222-2222-222222222222");

    private static IdentityDbContext CreateContext(Guid currentTenantId)
    {
        // Build an in-memory DB shared by both tenants
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase("TenantIsolationTest_" + Guid.NewGuid())
            .Options;

        var accessor = new FixedTenantContextAccessor(currentTenantId);
        return new IdentityDbContext(options, accessor);
    }

    private static async Task SeedBothTenantsAsync(IdentityDbContext context)
    {
        // Bypass filters to seed test data for both tenants
        var userA = TenantUser.Create(TenantA, "entra-a", "alice@a.com", "Alice");
        var userB = TenantUser.Create(TenantB, "entra-b", "bob@b.com", "Bob");

        // Directly set TenantId via reflection since it's protected
        typeof(CrmPlatform.ServiceTemplate.Domain.BaseEntity)
            .GetProperty("TenantId")!
            .SetValue(userA, TenantA);
        typeof(CrmPlatform.ServiceTemplate.Domain.BaseEntity)
            .GetProperty("TenantId")!
            .SetValue(userB, TenantB);

        context.TenantUsers.Add(userA);
        context.TenantUsers.Add(userB);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task ListUsers_OnlyReturnsTenantAUsers_WhenContextIsTenantA()
    {
        // Arrange: seed both tenants into the same in-memory DB
        var seedCtx = CreateContext(TenantA);
        var userA = TenantUser.Create(TenantA, "entra-a", "alice@a.com", "Alice");
        var userB = TenantUser.Create(TenantB, "entra-b", "bob@b.com", "Bob");

        // Use the underlying DB set bypassing filters for seed
        seedCtx.TenantUsers.Add(userA);
        seedCtx.TenantUsers.Add(userB);
        await seedCtx.SaveChangesAsync();

        // Act: query as TenantA
        var ctxA = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(seedCtx.Database.GetDbConnection().Database)
                .Options,
            new FixedTenantContextAccessor(TenantA));

        var users = await ctxA.TenantUsers.ToListAsync();

        // Assert: only TenantA's user returned
        users.Should().OnlyContain(u => u.TenantId == TenantA);
        users.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListUsers_OnlyReturnsTenantBUsers_WhenContextIsTenantB()
    {
        var dbName = "TenantIsolation_B_" + Guid.NewGuid();

        var userA = TenantUser.Create(TenantA, "entra-a", "alice@a.com", "Alice");
        var userB = TenantUser.Create(TenantB, "entra-b", "bob@b.com", "Bob");

        var seedCtx = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName).Options,
            new FixedTenantContextAccessor(TenantA));

        seedCtx.TenantUsers.Add(userA);
        seedCtx.TenantUsers.Add(userB);
        await seedCtx.SaveChangesAsync();

        var ctxB = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName).Options,
            new FixedTenantContextAccessor(TenantB));

        var users = await ctxB.TenantUsers.ToListAsync();

        users.Should().OnlyContain(u => u.TenantId == TenantB);
        users.Should().HaveCount(1);
    }

    [Fact]
    public async Task UserRoles_HiddenAcrossTenants()
    {
        var dbName = "TenantIsolation_Roles_" + Guid.NewGuid();

        var userA = TenantUser.Create(TenantA, "entra-a", "alice@a.com", "Alice");
        var roleA = UserRole.Grant(TenantA, userA.Id, TenantRoles.SalesRep, "admin");
        var userB = TenantUser.Create(TenantB, "entra-b", "bob@b.com", "Bob");
        var roleB = UserRole.Grant(TenantB, userB.Id, TenantRoles.TenantAdmin, "admin");

        var seedCtx = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName).Options,
            new FixedTenantContextAccessor(TenantA));

        seedCtx.TenantUsers.Add(userA);
        seedCtx.TenantUsers.Add(userB);
        seedCtx.UserRoles.Add(roleA);
        seedCtx.UserRoles.Add(roleB);
        await seedCtx.SaveChangesAsync();

        var ctxA = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName).Options,
            new FixedTenantContextAccessor(TenantA));

        var roles = await ctxA.UserRoles.ToListAsync();

        roles.Should().OnlyContain(r => r.TenantId == TenantA);
        roles.Should().HaveCount(1);
    }
}

/// <summary>Test double for ITenantContextAccessor with fixed tenant.</summary>
internal sealed class FixedTenantContextAccessor(Guid tenantId) : ITenantContextAccessor
{
    public Guid TenantId => tenantId;
}
