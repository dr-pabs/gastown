using CrmPlatform.SfaService.Domain.Entities;
using CrmPlatform.SfaService.Domain.Enums;
using CrmPlatform.SfaService.Domain.Events;
using FluentAssertions;

namespace CrmPlatform.SfaService.Tests.Domain;

public sealed class QuoteTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OppId    = Guid.NewGuid();

    private static Quote DefaultQuote() =>
        Quote.Create(TenantId, OppId, 10_000m, DateTime.UtcNow.AddDays(30));

    // ─── Send ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Send_BeforeNegotiateStage_Throws()
    {
        var quote = DefaultQuote();
        var act   = () => quote.Send(OpportunityStage.Propose); // below Negotiate
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Negotiate*");
    }

    [Fact]
    public void Send_AtNegotiateStage_Succeeds()
    {
        var quote = DefaultQuote();
        quote.Send(OpportunityStage.Negotiate);
        quote.Status.Should().Be(QuoteStatus.Sent);
        quote.DomainEvents.Should().ContainSingle(e => e is QuoteSentEvent);
    }

    [Fact]
    public void Send_AfterNegotiateStage_Succeeds()
    {
        var quote = DefaultQuote();
        quote.Send(OpportunityStage.Won); // Won > Negotiate
        quote.Status.Should().Be(QuoteStatus.Sent);
    }

    // ─── Accept / Reject ──────────────────────────────────────────────────────

    [Fact]
    public void Accept_AfterSend_Succeeds()
    {
        var quote = DefaultQuote();
        quote.Send(OpportunityStage.Negotiate);
        quote.Accept();
        quote.Status.Should().Be(QuoteStatus.Accepted);
    }

    [Fact]
    public void Reject_AfterSend_Succeeds()
    {
        var quote = DefaultQuote();
        quote.Send(OpportunityStage.Negotiate);
        quote.Reject();
        quote.Status.Should().Be(QuoteStatus.Rejected);
    }

    [Fact]
    public void Accept_OnDraftQuote_Throws()
    {
        var quote = DefaultQuote();
        var act   = () => quote.Accept();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Reject_OnDraftQuote_Throws()
    {
        var quote = DefaultQuote();
        var act   = () => quote.Reject();
        act.Should().Throw<InvalidOperationException>();
    }

    // ─── UpdateLineItems ──────────────────────────────────────────────────────

    [Fact]
    public void UpdateLineItems_OnDraft_Succeeds()
    {
        var quote = DefaultQuote();
        quote.UpdateLineItems("""[{"item":"widget","qty":2}]""", 500m);
        quote.TotalValue.Should().Be(500m);
    }

    [Fact]
    public void UpdateLineItems_AfterSend_Throws()
    {
        var quote = DefaultQuote();
        quote.Send(OpportunityStage.Negotiate);
        var act = () => quote.UpdateLineItems("[]", 0m);
        act.Should().Throw<InvalidOperationException>();
    }
}
