using CrmPlatform.CssService.Application.Cases;
using CrmPlatform.CssService.Domain.Entities;
using CrmPlatform.CssService.Domain.Enums;
using CrmPlatform.CssService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CrmPlatform.CssService.Tests.Application;

public sealed class ClosedCaseImmutabilityTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId   = Guid.NewGuid();

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

    private static async Task<Case> SeedClosedCaseAsync(CssDbContext db)
    {
        var c = Case.Create(TenantId, "Closed Case", "Desc", CasePriority.Low,
            CaseChannel.Email, null, null, UserId);
        c.Open(DateTime.UtcNow.AddHours(8));
        c.Resolve();
        c.Close();
        db.Cases.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    [Fact]
    public async Task TransitionStatus_OnClosedCase_Returns422()
    {
        await using var db = BuildDb();
        var c              = await SeedClosedCaseAsync(db);
        var handler        = new TransitionStatusHandler(db, Mock.Of<ServiceBusEventPublisher>());

        var result = await handler.HandleAsync(
            new TransitionStatusCommand(c.Id, CaseStatus.Open));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCode.ValidationError);
    }

    [Fact]
    public async Task AssignCase_OnClosedCase_Returns422()
    {
        await using var db = BuildDb();
        var c              = await SeedClosedCaseAsync(db);
        var handler        = new AssignCaseHandler(db, BuildCtx(), Mock.Of<ServiceBusEventPublisher>());

        var result = await handler.HandleAsync(new AssignCaseCommand(c.Id, Guid.NewGuid()));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCode.ValidationError);
    }

    [Fact]
    public async Task AddComment_OnClosedCase_Returns422()
    {
        await using var db = BuildDb();
        var c              = await SeedClosedCaseAsync(db);
        var handler        = new AddCommentHandler(db, BuildCtx());

        var result = await handler.HandleAsync(
            new AddCommentCommand(c.Id, "Late comment", false, CommentAuthorType.Staff));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCode.ValidationError);
    }

    [Fact]
    public async Task EscalateCase_OnClosedCase_Returns422()
    {
        await using var db = BuildDb();
        var c              = await SeedClosedCaseAsync(db);
        var handler        = new EscalateCaseHandler(db, BuildCtx(), Mock.Of<ServiceBusEventPublisher>());

        var result = await handler.HandleAsync(
            new EscalateCaseCommand(c.Id, "Reason", null));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCode.ValidationError);
    }
}
