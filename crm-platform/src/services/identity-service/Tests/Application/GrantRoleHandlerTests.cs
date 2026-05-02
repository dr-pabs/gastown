using CrmPlatform.IdentityService.Application.Roles;
using CrmPlatform.IdentityService.Domain.Entities;
using CrmPlatform.IdentityService.Domain.Enums;
using CrmPlatform.IdentityService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Azure.Messaging.ServiceBus;

namespace CrmPlatform.IdentityService.Tests.Application;

public sealed class GrantRoleHandlerTests
{
    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");

    private static IdentityDbContext CreateContext(string dbName) =>
        new(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName).Options,
            new FixedGrantRoleTenantContextAccessor(TenantA));

    private static ServiceBusEventPublisher CreatePublisher()
    {
        var mockClient = new Mock<ServiceBusClient>();
        var mockSender = new Mock<ServiceBusSender>();
        mockClient.Setup(c => c.CreateSender(It.IsAny<string>())).Returns(mockSender.Object);
        return new ServiceBusEventPublisher(mockClient.Object, NullLogger<ServiceBusEventPublisher>.Instance);
    }

    private static async Task<TenantUser> SeedUserAsync(IdentityDbContext db)
    {
        var user = TenantUser.Create(TenantA, "entra-1", "alice@a.com", "Alice");
        db.TenantUsers.Add(user);
        await db.SaveChangesAsync();
        user.ClearDomainEvents();
        return user;
    }

    [Fact]
    public async Task HandleAsync_ValidRole_GrantsRole()
    {
        var dbName = "GrantRole_Valid_" + Guid.NewGuid();
        var db = CreateContext(dbName);
        var user = await SeedUserAsync(db);

        var handler = new GrantRoleHandler(db, CreatePublisher(), NullLogger<GrantRoleHandler>.Instance);
        var result = await handler.HandleAsync(
            new GrantRoleCommand(TenantA, user.Id, TenantRoles.SalesRep, "admin"));

        result.IsSuccess.Should().BeTrue();

        var role = await db.UserRoles.FirstOrDefaultAsync(r => r.TenantUserId == user.Id);
        role.Should().NotBeNull();
        role!.Role.Should().Be(TenantRoles.SalesRep);
    }

    [Fact]
    public async Task HandleAsync_PlatformAdmin_ReturnsValidationError()
    {
        var dbName = "GrantRole_PlatformAdmin_" + Guid.NewGuid();
        var db = CreateContext(dbName);
        var user = await SeedUserAsync(db);

        var handler = new GrantRoleHandler(db, CreatePublisher(), NullLogger<GrantRoleHandler>.Instance);
        var result = await handler.HandleAsync(
            new GrantRoleCommand(TenantA, user.Id, "PlatformAdmin", "admin"));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(ResultErrorCode.ValidationError);
    }

    [Fact]
    public async Task HandleAsync_UserNotFound_ReturnsNotFound()
    {
        var db = CreateContext("GrantRole_NotFound_" + Guid.NewGuid());
        var handler = new GrantRoleHandler(db, CreatePublisher(), NullLogger<GrantRoleHandler>.Instance);

        var result = await handler.HandleAsync(
            new GrantRoleCommand(TenantA, Guid.NewGuid(), TenantRoles.SalesRep, "admin"));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(ResultErrorCode.NotFound);
    }

    [Fact]
    public async Task HandleAsync_DuplicateGrant_IsIdempotent()
    {
        var dbName = "GrantRole_Idempotent_" + Guid.NewGuid();
        var db = CreateContext(dbName);
        var user = await SeedUserAsync(db);
        var handler = new GrantRoleHandler(db, CreatePublisher(), NullLogger<GrantRoleHandler>.Instance);

        var first  = await handler.HandleAsync(new GrantRoleCommand(TenantA, user.Id, TenantRoles.SalesRep, "admin"));
        var second = await handler.HandleAsync(new GrantRoleCommand(TenantA, user.Id, TenantRoles.SalesRep, "admin"));

        second.IsSuccess.Should().BeTrue();
        second.Value!.RoleId.Should().Be(first.Value!.RoleId);

        var count = await db.UserRoles.CountAsync(r => r.TenantUserId == user.Id && r.Role == TenantRoles.SalesRep);
        count.Should().Be(1);
    }
}

internal sealed class FixedGrantRoleTenantContextAccessor(Guid tenantId) : ITenantContextAccessor
{
    public Guid TenantId => tenantId;
}
