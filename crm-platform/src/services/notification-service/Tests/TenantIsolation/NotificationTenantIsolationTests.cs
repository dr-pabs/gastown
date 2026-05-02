using CrmPlatform.NotificationService.Domain.Entities;
using CrmPlatform.NotificationService.Domain.Enums;
using CrmPlatform.NotificationService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace CrmPlatform.NotificationService.Tests.TenantIsolation;

/// <summary>
/// Verifies that global query filters on NotificationDbContext prevent cross-tenant data leakage.
/// Uses shared in-memory database names with per-tenant ITenantContextAccessor mocks.
/// </summary>
[Trait("Category", "TenantIsolation")]
public sealed class NotificationTenantIsolationTests
{
    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = new("22222222-2222-2222-2222-222222222222");

    private static NotificationDbContext CreateContext(string dbName, Guid tenantId)
    {
        var opts = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var accessor = new Mock<ITenantContextAccessor>();
        accessor.Setup(a => a.TenantId).Returns(tenantId);
        return new NotificationDbContext(opts, accessor.Object);
    }

    // ─── In-App inbox isolation ───────────────────────────────────────────────

    [Fact]
    public async Task TenantBInAppItems_NotVisibleTo_TenantA()
    {
        var db           = Guid.NewGuid().ToString();
        var sharedUserId = Guid.NewGuid();

        using var ctxA = CreateContext(db, TenantA);
        using var ctxB = CreateContext(db, TenantB);

        ctxA.InAppItems.Add(InAppNotification.Create(
            TenantA, sharedUserId, "TenantA Message", "You have a case in TenantA",
            NotificationCategory.CaseAssigned));
        ctxB.InAppItems.Add(InAppNotification.Create(
            TenantB, sharedUserId, "TenantB Message", "You have a case in TenantB",
            NotificationCategory.CaseAssigned));

        await ctxA.SaveChangesAsync();
        await ctxB.SaveChangesAsync();

        var tenantAItems = await ctxA.InAppItems.ToListAsync();
        tenantAItems.Should().HaveCount(1);
        tenantAItems[0].Title.Should().Be("TenantA Message");
        tenantAItems.Should().NotContain(x => x.Title == "TenantB Message");
    }

    // ─── Soft-delete filter ───────────────────────────────────────────────────

    [Fact]
    public async Task SoftDeleted_InAppItems_NotReturnedByDefault()
    {
        var db     = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();

        using var ctx = CreateContext(db, TenantA);

        var item = InAppNotification.Create(
            TenantA, userId, "Active", "body", NotificationCategory.General);
        var deleted = InAppNotification.Create(
            TenantA, userId, "Deleted", "body", NotificationCategory.General);

        deleted.Delete();

        ctx.InAppItems.AddRange(item, deleted);
        await ctx.SaveChangesAsync();

        var visible = await ctx.InAppItems.ToListAsync();
        visible.Should().HaveCount(1);
        visible[0].Title.Should().Be("Active");
    }

    // ─── Preferences isolation ────────────────────────────────────────────────

    [Fact]
    public async Task TenantB_Preferences_NotVisibleTo_TenantA()
    {
        var db     = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();

        using var ctxA = CreateContext(db, TenantA);
        using var ctxB = CreateContext(db, TenantB);

        ctxA.Preferences.Add(NotificationPreference.Create(
            TenantA, userId, NotificationChannel.Email, NotificationCategory.General, isEnabled: true));
        ctxB.Preferences.Add(NotificationPreference.Create(
            TenantB, userId, NotificationChannel.Email, NotificationCategory.General, isEnabled: false));

        await ctxA.SaveChangesAsync();
        await ctxB.SaveChangesAsync();

        var aPrefs = await ctxA.Preferences.ToListAsync();
        aPrefs.Should().HaveCount(1);
        aPrefs[0].IsEnabled.Should().BeTrue();

        var bPrefs = await ctxB.Preferences.ToListAsync();
        bPrefs.Should().HaveCount(1);
        bPrefs[0].IsEnabled.Should().BeFalse();
    }

    // ─── Records isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task TenantB_Records_NotVisibleTo_TenantA()
    {
        var db     = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();

        using var ctxA = CreateContext(db, TenantA);
        using var ctxB = CreateContext(db, TenantB);

        ctxA.Records.Add(NotificationRecord.CreateQueued(
            TenantA, userId, "a@a.com",
            NotificationChannel.Email, NotificationCategory.General, "TenantA body"));
        ctxB.Records.Add(NotificationRecord.CreateQueued(
            TenantB, userId, "b@b.com",
            NotificationChannel.Email, NotificationCategory.General, "TenantB body"));

        await ctxA.SaveChangesAsync();
        await ctxB.SaveChangesAsync();

        var aRecords = await ctxA.Records.ToListAsync();
        aRecords.Should().HaveCount(1);
        aRecords[0].Body.Should().Be("TenantA body");
    }
}
