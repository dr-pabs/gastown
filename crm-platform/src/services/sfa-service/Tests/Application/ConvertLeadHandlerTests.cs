using CrmPlatform.SfaService.Application.Leads;
using CrmPlatform.SfaService.Domain.Enums;
using CrmPlatform.SfaService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CrmPlatform.SfaService.Tests.Application;

public sealed class ConvertLeadHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId   = Guid.NewGuid();

    private static SfaDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<SfaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var accessor = new Mock<ITenantContextAccessor>();
        accessor.Setup(a => a.TenantId).Returns(TenantId);
        return new SfaDbContext(opts, accessor.Object);
    }

    private static ITenantContext BuildTenantContext() =>
        Mock.Of<ITenantContext>(c => c.TenantId == TenantId && c.UserId == UserId);

    private ConvertLeadHandler BuildHandler(SfaDbContext db) =>
        new(db, BuildTenantContext(), Mock.Of<ServiceBusEventPublisher>());

    // ─── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ConvertLead_HappyPath_ReturnsOpportunityId()
    {
        await using var db = BuildDb();
        var lead = Domain.Entities.Lead.Create(
            TenantId, "Test Lead", "test@example.com", null, null, LeadSource.Web, UserId);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        var result  = await handler.HandleAsync(new ConvertLeadCommand(
            lead.Id, "New Opp", 25_000m, null, null, null));

        result.IsSuccess.Should().BeTrue();
        result.Value.OpportunityId.Should().NotBeEmpty();

        var refreshedLead = await db.Leads
            .IgnoreQueryFilters()
            .FirstAsync(l => l.Id == lead.Id);
        refreshedLead.IsConverted.Should().BeTrue();
        refreshedLead.ConvertedToOpportunityId.Should().Be(result.Value.OpportunityId);
    }

    [Fact]
    public async Task ConvertLead_CreatesOpportunityRecord()
    {
        await using var db = BuildDb();
        var lead = Domain.Entities.Lead.Create(
            TenantId, "Test Lead 2", "lead2@example.com", null, null, LeadSource.Partner, UserId);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        var result  = await handler.HandleAsync(new ConvertLeadCommand(
            lead.Id, "Big Deal", 100_000m, null, null, null));

        result.IsSuccess.Should().BeTrue();
        var opp = await db.Opportunities.FirstOrDefaultAsync(o => o.Id == result.Value.OpportunityId);
        opp.Should().NotBeNull();
        opp!.Title.Should().Be("Big Deal");
        opp.ConvertedFromLeadId.Should().Be(lead.Id);
    }

    // ─── Idempotency guard ────────────────────────────────────────────────────

    [Fact]
    public async Task ConvertLead_AlreadyConverted_ReturnsConflict()
    {
        await using var db = BuildDb();
        var lead = Domain.Entities.Lead.Create(
            TenantId, "Already Converted", "dup@example.com", null, null, LeadSource.Campaign, UserId);
        lead.MarkConverted(Guid.NewGuid());
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        var result  = await handler.HandleAsync(new ConvertLeadCommand(
            lead.Id, "Should Fail", 1m, null, null, null));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCode.Conflict);
    }

    // ─── Not found ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConvertLead_NotFound_ReturnsNotFound()
    {
        await using var db = BuildDb();
        var handler = BuildHandler(db);
        var result  = await handler.HandleAsync(new ConvertLeadCommand(
            Guid.NewGuid(), "Ghost", 0m, null, null, null));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCode.NotFound);
    }
}
