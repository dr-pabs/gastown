using CrmPlatform.SfaService.Domain.Entities;
using CrmPlatform.SfaService.Domain.Enums;
using CrmPlatform.SfaService.Domain.Events;
using FluentAssertions;

namespace CrmPlatform.SfaService.Tests.Domain;

public sealed class LeadTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId   = Guid.NewGuid();

    private static Lead DefaultLead() =>
        Lead.Create(TenantId, "Alice Smith", "alice@example.com",
            "+1-555-0100", "Acme Corp", LeadSource.Web, UserId);

    // ─── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsInitialValues()
    {
        var lead = DefaultLead();

        lead.Name.Should().Be("Alice Smith");
        lead.Email.Should().Be("alice@example.com");
        lead.Status.Should().Be(LeadStatus.New);
        lead.Score.Should().Be(0);
        lead.IsConverted.Should().BeFalse();
    }

    [Fact]
    public void Create_RaisesLeadCreatedEvent()
    {
        var lead = DefaultLead();
        lead.DomainEvents.Should().ContainSingle(e => e is LeadCreatedEvent);
    }

    [Fact]
    public void Create_EmailIsLowercased()
    {
        var lead = Lead.Create(TenantId, "Bob", "Bob@EXAMPLE.COM", null, null, LeadSource.Referral, UserId);
        lead.Email.Should().Be("bob@example.com");
    }

    // ─── Assign ───────────────────────────────────────────────────────────────

    [Fact]
    public void Assign_SetsContactedStatus_WhenNew()
    {
        var lead = DefaultLead();
        lead.Assign(Guid.NewGuid(), UserId);
        lead.Status.Should().Be(LeadStatus.Contacted);
    }

    [Fact]
    public void Assign_DoesNotDowngradeStatus_WhenAlreadyQualified()
    {
        var lead = DefaultLead();
        lead.Qualify();
        lead.Assign(Guid.NewGuid(), UserId);
        lead.Status.Should().Be(LeadStatus.Qualified);
    }

    [Fact]
    public void Assign_RaisesLeadAssignedEvent()
    {
        var lead   = DefaultLead();
        var rep    = Guid.NewGuid();
        lead.Assign(rep, UserId);
        lead.DomainEvents.Should().Contain(e => e is LeadAssignedEvent la && la.AssignedToUserId == rep);
    }

    // ─── UpdateScore ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void UpdateScore_AcceptsValidRange(int score)
    {
        var lead = DefaultLead();
        lead.UpdateScore(score);
        lead.Score.Should().Be(score);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void UpdateScore_ThrowsForOutOfRange(int score)
    {
        var lead = DefaultLead();
        var act  = () => lead.UpdateScore(score);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ─── Qualify / Disqualify ─────────────────────────────────────────────────

    [Fact]
    public void Qualify_SetsQualifiedStatus()
    {
        var lead = DefaultLead();
        lead.Qualify();
        lead.Status.Should().Be(LeadStatus.Qualified);
    }

    [Fact]
    public void Qualify_ThrowsWhenConverted()
    {
        var lead = DefaultLead();
        lead.MarkConverted(Guid.NewGuid());
        var act = () => lead.Qualify();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Disqualify_SoftDeletesLead()
    {
        var lead = DefaultLead();
        lead.Disqualify();
        lead.IsDeleted.Should().BeTrue();
        lead.Status.Should().Be(LeadStatus.Disqualified);
    }

    // ─── MarkConverted ────────────────────────────────────────────────────────

    [Fact]
    public void MarkConverted_SetsIsConvertedAndRaisesEvent()
    {
        var lead    = DefaultLead();
        var oppId   = Guid.NewGuid();
        lead.MarkConverted(oppId);

        lead.IsConverted.Should().BeTrue();
        lead.ConvertedToOpportunityId.Should().Be(oppId);
        lead.DomainEvents.Should().Contain(e => e is LeadConvertedEvent lc && lc.OpportunityId == oppId);
    }

    [Fact]
    public void MarkConverted_ThrowsOnSecondConvert()
    {
        var lead = DefaultLead();
        lead.MarkConverted(Guid.NewGuid());

        var act = () => lead.MarkConverted(Guid.NewGuid());
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already been converted*");
    }
}
