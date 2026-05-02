using CrmPlatform.IdentityService.Application.Users;
using CrmPlatform.IdentityService.Domain.Entities;
using CrmPlatform.IdentityService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Azure.Messaging.ServiceBus;

namespace CrmPlatform.IdentityService.Tests.Application;

public sealed class ProvisionUserHandlerTests
{
    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");

    private static IdentityDbContext CreateContext(string dbName) =>
        new(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName).Options,
            new FixedTenantContextAccessor(TenantA));

    private static ServiceBusEventPublisher CreatePublisher()
    {
        var mockClient = new Mock<ServiceBusClient>();
        var mockSender = new Mock<ServiceBusSender>();
        mockClient.Setup(c => c.CreateSender(It.IsAny<string>())).Returns(mockSender.Object);
        return new ServiceBusEventPublisher(mockClient.Object, NullLogger<ServiceBusEventPublisher>.Instance);
    }

    [Fact]
    public async Task HandleAsync_NewUser_ReturnsOkAndCreatesRecord()
    {
        var db = CreateContext("ProvisionUser_New_" + Guid.NewGuid());
        var handler = new ProvisionUserHandler(db, CreatePublisher(), NullLogger<ProvisionUserHandler>.Instance);

        var command = new ProvisionUserCommand(TenantA, "entra-abc", "alice@a.com", "Alice", "admin");

        var result = await handler.HandleAsync(command);

        result.IsSuccess.Should().BeTrue();
        result.Value!.UserId.Should().NotBeEmpty();

        var stored = await db.TenantUsers.FirstOrDefaultAsync(u => u.EntraObjectId == "entra-abc");
        stored.Should().NotBeNull();
        stored!.Email.Should().Be("alice@a.com");
    }

    [Fact]
    public async Task HandleAsync_DuplicateEntraObjectId_ReturnsExistingUser()
    {
        var dbName = "ProvisionUser_Dup_" + Guid.NewGuid();
        var db = CreateContext(dbName);
        var handler = new ProvisionUserHandler(db, CreatePublisher(), NullLogger<ProvisionUserHandler>.Instance);

        var command = new ProvisionUserCommand(TenantA, "entra-abc", "alice@a.com", "Alice", "admin");
        var first  = await handler.HandleAsync(command);
        var second = await handler.HandleAsync(command);

        second.IsSuccess.Should().BeTrue();
        second.Value!.UserId.Should().Be(first.Value!.UserId, "idempotent provisioning");

        var count = await db.TenantUsers.IgnoreQueryFilters()
            .CountAsync(u => u.EntraObjectId == "entra-abc");
        count.Should().Be(1, "must not create duplicate records");
    }

    [Fact]
    public async Task HandleAsync_WritesProvisioningLog()
    {
        var db = CreateContext("ProvisionUser_Log_" + Guid.NewGuid());
        var handler = new ProvisionUserHandler(db, CreatePublisher(), NullLogger<ProvisionUserHandler>.Instance);

        var command = new ProvisionUserCommand(TenantA, "entra-def", "bob@a.com", "Bob", "admin@a.com");
        var result = await handler.HandleAsync(command);

        var log = await db.UserProvisioningLogs.FirstOrDefaultAsync(l => l.TenantUserId == result.Value!.UserId);
        log.Should().NotBeNull();
        log!.InitiatedBy.Should().Be("admin@a.com");
    }
}

internal sealed class FixedTenantContextAccessor(Guid tenantId) : ITenantContextAccessor
{
    public Guid TenantId => tenantId;
}
