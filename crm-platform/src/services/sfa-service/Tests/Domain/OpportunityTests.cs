using CrmPlatform.SfaService.Domain.Entities;
using CrmPlatform.SfaService.Domain.Enums;
using CrmPlatform.SfaService.Domain.Events;
using FluentAssertions;

namespace CrmPlatform.SfaService.Tests.Domain;

public sealed class OpportunityTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static Opportunity DefaultOpportunity() =>
        Opportunity.Create(TenantId, "Enterprise Deal", 50_000m, null, null, null);

    // ─── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_StartsAtQualifyStage()
    {
        var opp = DefaultOpportunity();
        opp.Stage.Should().Be(OpportunityStage.Qualify);
    }

    [Fact]
    public void Create_ThrowsForNegativeValue()
    {
        var act = () => Opportunity.Create(TenantId, "Deal", -1m, null, null, null);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ─── AdvanceStage — sequential ────────────────────────────────────────────

    [Fact]
    public void AdvanceStage_Qualify_To_Propose_Succeeds()
    {
        var opp = DefaultOpportunity();
        opp.AdvanceStage(OpportunityStage.Propose);
        opp.Stage.Should().Be(OpportunityStage.Propose);
    }

    [Fact]
    public void AdvanceStage_Sequential_RaisesStageChangedEvent()
    {
        var opp = DefaultOpportunity();
        opp.AdvanceStage(OpportunityStage.Propose);
        opp.DomainEvents.Should().Contain(e => e is OpportunityStageChangedEvent sc
            && sc.PreviousStage == OpportunityStage.Qualify
            && sc.NewStage      == OpportunityStage.Propose);
    }

    [Fact]
    public void AdvanceStage_Skipping_Throws()
    {
        var opp = DefaultOpportunity();
        var act = () => opp.AdvanceStage(OpportunityStage.Negotiate); // skips Propose
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*sequentially*");
    }

    // ─── AdvanceStage — terminal ──────────────────────────────────────────────

    [Fact]
    public void AdvanceStage_ToWon_FromAnyNonTerminalStage()
    {
        var opp = DefaultOpportunity();
        opp.AdvanceStage(OpportunityStage.Won);
        opp.Stage.Should().Be(OpportunityStage.Won);
        opp.DomainEvents.Should().Contain(e => e is OpportunityWonEvent);
    }

    [Fact]
    public void AdvanceStage_ToLost_FromAnyNonTerminalStage()
    {
        var opp = DefaultOpportunity();
        opp.AdvanceStage(OpportunityStage.Lost);
        opp.Stage.Should().Be(OpportunityStage.Lost);
        opp.DomainEvents.Should().Contain(e => e is OpportunityLostEvent);
    }

    [Fact]
    public void AdvanceStage_FromTerminal_Throws()
    {
        var opp = DefaultOpportunity();
        opp.AdvanceStage(OpportunityStage.Won);

        var act = () => opp.AdvanceStage(OpportunityStage.Lost);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*terminal*");
    }

    // ─── Full sequential path ─────────────────────────────────────────────────

    [Fact]
    public void AdvanceStage_FullPath_QualifyToNegotiate()
    {
        var opp = DefaultOpportunity();
        opp.AdvanceStage(OpportunityStage.Propose);
        opp.AdvanceStage(OpportunityStage.Negotiate);
        opp.Stage.Should().Be(OpportunityStage.Negotiate);
    }
}
