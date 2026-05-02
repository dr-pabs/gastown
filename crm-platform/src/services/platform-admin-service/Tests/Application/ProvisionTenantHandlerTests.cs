using Azure.Messaging.ServiceBus;
using CrmPlatform.PlatformAdminService.Application.Tenants;
using CrmPlatform.PlatformAdminService.Domain.Enums;
using CrmPlatform.PlatformAdminService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrmPlatform.PlatformAdminService.Tests.Application;

public sealed class ProvisionTenantHandlerTests
{
    // platform-admin service uses a platform-level tenant context (itself is the platform tenant)
    private static readonly Guid PlatformTenantId = new("00000000-0000-0000-0000-000000000001");

    private static PlatformDbContext CreateContext(string dbName) =>
        new(
            new DbContextOptionsBuilder<PlatformDbContext>()
                .UseInMemoryDatabase(dbName).Options,
            new FixedTenantContextAccessor(PlatformTenantId));

    private static ServiceBusEventPublisher CreatePublisher()
    {
        var mockClient = new Mock<ServiceBusClient>();
        var mockSender = new Mock<ServiceBusSender>();
        mockClient.Setup(c => c.CreateSender(It.IsAny<string>())).Returns(mockSender.Object);
        return new ServiceBusEventPublisher(mockClient.Object, NullLogger<ServiceBusEventPublisher>.Instance);
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_NewTenant_ReturnsOkAndPersistsTenant()
    {
        var db      = CreateContext("ProvisionTenant_New_" + Guid.NewGuid());
        var handler = new ProvisionTenantHandler(db, CreatePublisher(), NullLogger<ProvisionTenantHandler>.Instance);

        var result = await handler.HandleAsync(new ProvisionTenantCommand("Acme Corp", "acme", "starter", "admin"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Slug.Should().Be("acme");

        var tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Slug == "acme");
        tenant.Should().NotBeNull();
        tenant!.Status.Should().Be(TenantStatus.Active);
        tenant.Name.Should().Be("Acme Corp");
    }

    [Fact]
    public async Task HandleAsync_NewTenant_WritesProvisioningLogs()
    {
        var db      = CreateContext("ProvisionTenant_Logs_" + Guid.NewGuid());
        var handler = new ProvisionTenantHandler(db, CreatePublisher(), NullLogger<ProvisionTenantHandler>.Instance);

        var result = await handler.HandleAsync(new ProvisionTenantCommand("Beta", "beta", "pro", "admin"));

        result.IsSuccess.Should().BeTrue();

        var logs = await db.ProvisioningLogs.IgnoreQueryFilters().ToListAsync();
        logs.Should().NotBeEmpty();
        logs.Should().Contain(l => l.Step == "CreateTenantDatabaseRecord");
        logs.Should().Contain(l => l.Step == "SetTenantStatusActive");
    }

    // ── Slug conflict ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_DuplicateSlug_ReturnsConflict()
    {
        var db      = CreateContext("ProvisionTenant_Conflict_" + Guid.NewGuid());
        var handler = new ProvisionTenantHandler(db, CreatePublisher(), NullLogger<ProvisionTenantHandler>.Instance);

        await handler.HandleAsync(new ProvisionTenantCommand("First", "dupe", "starter", "admin"));
        var result = await handler.HandleAsync(new ProvisionTenantCommand("Second", "dupe", "pro", "admin"));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(ResultErrorCode.Conflict);
    }

    [Fact]
    public async Task HandleAsync_DuplicateSlug_CaseInsensitive()
    {
        var db      = CreateContext("ProvisionTenant_SlugCase_" + Guid.NewGuid());
        var handler = new ProvisionTenantHandler(db, CreatePublisher(), NullLogger<ProvisionTenantHandler>.Instance);

        await handler.HandleAsync(new ProvisionTenantCommand("First", "MySlug", "starter", "admin"));
        var result = await handler.HandleAsync(new ProvisionTenantCommand("Second", "MYSLUG", "pro", "admin"));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(ResultErrorCode.Conflict);
    }

    // ── Tenant status flow ────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_ProvisionedTenant_StatusIsActive()
    {
        var db      = CreateContext("ProvisionTenant_Status_" + Guid.NewGuid());
        var handler = new ProvisionTenantHandler(db, CreatePublisher(), NullLogger<ProvisionTenantHandler>.Instance);

        var result = await handler.HandleAsync(new ProvisionTenantCommand("Gamma", "gamma", "enterprise", "admin"));

        result.IsSuccess.Should().BeTrue();

        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == result.Value!.TenantId);
        tenant.Status.Should().Be(TenantStatus.Active);
    }
}
