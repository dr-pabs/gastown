using CrmPlatform.CssService.Application.Cases;
using CrmPlatform.CssService.Domain.Entities;
using CrmPlatform.CssService.Domain.Enums;
using CrmPlatform.CssService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CrmPlatform.CssService.Tests.Application;

public sealed class CloseCaseHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static CssDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<CssDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var accessor = new Mock<ITenantContextAccessor>();
        accessor.Setup(a => a.TenantId).Returns(TenantId);
        return new CssDbContext(opts, accessor.Object);
    }

    private static ITenantContext BuildCtx() =>
        Mock.Of<ITenantContext>(c => c.TenantId == TenantId && c.UserId == UserId);

    [Fact]
    public async Task HandleAsync_ResolvesAndCloses_OpenCase()
    {
        await using var db = BuildDb();
        var supportCase = Case.Create(TenantId, "Customer close", "Desc", CasePriority.Medium,
            CaseChannel.Portal, UserId, null, UserId);
        supportCase.Open(DateTime.UtcNow.AddHours(4));
        db.Cases.Add(supportCase);
        await db.SaveChangesAsync();

        var handler = new CloseCaseHandler(db, BuildCtx(), Mock.Of<ServiceBusEventPublisher>());

        var result = await handler.HandleAsync(new CloseCaseCommand(supportCase.Id));

        result.IsSuccess.Should().BeTrue();
        supportCase.Status.Should().Be(CaseStatus.Closed);
    }

    [Fact]
    public async Task HandleAsync_IsIdempotent_ForClosedCase()
    {
        await using var db = BuildDb();
        var supportCase = Case.Create(TenantId, "Already closed", "Desc", CasePriority.Low,
            CaseChannel.Portal, UserId, null, UserId);
        supportCase.Open(DateTime.UtcNow.AddHours(4));
        supportCase.Resolve();
        supportCase.Close();
        db.Cases.Add(supportCase);
        await db.SaveChangesAsync();

        var handler = new CloseCaseHandler(db, BuildCtx(), Mock.Of<ServiceBusEventPublisher>());

        var result = await handler.HandleAsync(new CloseCaseCommand(supportCase.Id));

        result.IsSuccess.Should().BeTrue();
        supportCase.Status.Should().Be(CaseStatus.Closed);
    }
}
