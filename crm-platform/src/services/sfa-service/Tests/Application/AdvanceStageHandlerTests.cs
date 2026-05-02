using CrmPlatform.SfaService.Application.Opportunities;
using CrmPlatform.SfaService.Domain.Entities;
using CrmPlatform.SfaService.Domain.Enums;
using CrmPlatform.SfaService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CrmPlatform.SfaService.Tests.Application;

public sealed class AdvanceStageHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static SfaDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<SfaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var accessor = new Mock<ITenantContextAccessor>();
        accessor.Setup(a => a.TenantId).Returns(TenantId);
        return new SfaDbContext(opts, accessor.Object);
    }

    private AdvanceStageHandler BuildHandler(SfaDbContext db) =>
        new(db, Mock.Of<ServiceBusEventPublisher>());

    private static async Task<Opportunity> SeedOpportunityAsync(SfaDbContext db)
    {
        var opp = Opportunity.Create(TenantId, "Deal", 10_000m, null, null, null);
        db.Opportunities.Add(opp);
        await db.SaveChangesAsync();
        return opp;
    }

    [Fact]
    public async Task AdvanceStage_Sequential_Succeeds()
    {
        await using var db  = BuildDb();
        var opp             = await SeedOpportunityAsync(db);
        var handler         = BuildHandler(db);

        var result = await handler.HandleAsync(new AdvanceStageCommand(opp.Id, OpportunityStage.Propose));

        result.IsSuccess.Should().BeTrue();
        var refreshed = await db.Opportunities.FindAsync(opp.Id);
        refreshed!.Stage.Should().Be(OpportunityStage.Propose);
    }

    [Fact]
    public async Task AdvanceStage_Skip_ReturnsValidationError()
    {
        await using var db = BuildDb();
        var opp            = await SeedOpportunityAsync(db);
        var handler        = BuildHandler(db);

        // Qualify → Negotiate skips Propose
        var result = await handler.HandleAsync(new AdvanceStageCommand(opp.Id, OpportunityStage.Negotiate));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCode.ValidationError);
    }

    [Fact]
    public async Task AdvanceStage_ToWon_FromQualify_Succeeds()
    {
        await using var db = BuildDb();
        var opp            = await SeedOpportunityAsync(db);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(new AdvanceStageCommand(opp.Id, OpportunityStage.Won));

        result.IsSuccess.Should().BeTrue();
        var refreshed = await db.Opportunities.FindAsync(opp.Id);
        refreshed!.Stage.Should().Be(OpportunityStage.Won);
    }

    [Fact]
    public async Task AdvanceStage_FromTerminal_ReturnsValidationError()
    {
        await using var db = BuildDb();
        var opp            = await SeedOpportunityAsync(db);
        var handler        = BuildHandler(db);

        await handler.HandleAsync(new AdvanceStageCommand(opp.Id, OpportunityStage.Won));

        // Second advance on a terminal opportunity
        var result = await handler.HandleAsync(
            new AdvanceStageCommand(opp.Id, OpportunityStage.Lost));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCode.ValidationError);
    }

    [Fact]
    public async Task AdvanceStage_NotFound_ReturnsNotFound()
    {
        await using var db = BuildDb();
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            new AdvanceStageCommand(Guid.NewGuid(), OpportunityStage.Propose));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCode.NotFound);
    }
}
