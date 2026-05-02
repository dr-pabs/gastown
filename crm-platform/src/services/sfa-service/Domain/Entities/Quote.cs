using CrmPlatform.SfaService.Domain.Enums;
using CrmPlatform.SfaService.Domain.Events;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.SfaService.Domain.Entities;

/// <summary>
/// A quote linked to an opportunity.
/// May only be sent when the linked Opportunity is in Negotiate stage or later.
/// </summary>
public sealed class Quote : BaseEntity
{
    private Quote() { } // EF Core

    public Guid        OpportunityId  { get; private set; }
    public string      LineItemsJson  { get; private set; } = "[]";  // serialised JSON
    public decimal     TotalValue     { get; private set; }
    public QuoteStatus Status         { get; private set; } = QuoteStatus.Draft;
    public DateTime?   ValidUntil     { get; private set; }

    // Navigation
    public Opportunity? Opportunity { get; private set; }

    public static Quote Create(Guid tenantId, Guid opportunityId, decimal totalValue, DateTime? validUntil)
    {
        if (totalValue < 0) throw new ArgumentOutOfRangeException(nameof(totalValue));
        return new Quote
        {
            TenantId      = tenantId,
            OpportunityId = opportunityId,
            TotalValue    = totalValue,
            ValidUntil    = validUntil,
            Status        = QuoteStatus.Draft,
        };
    }

    /// <summary>Marks the quote as Sent. Opportunity must be in Negotiate or Won stage.</summary>
    public void Send(OpportunityStage opportunityStage)
    {
        if (opportunityStage < OpportunityStage.Negotiate)
            throw new InvalidOperationException(
                "A quote may only be sent when the opportunity is in Negotiate stage or later.");
        if (Status != QuoteStatus.Draft)
            throw new InvalidOperationException($"Cannot send a quote in status {Status}.");

        Status = QuoteStatus.Sent;
        AddDomainEvent(new QuoteSentEvent(Id, TenantId, OpportunityId));
    }

    public void Accept()
    {
        if (Status != QuoteStatus.Sent)
            throw new InvalidOperationException($"Cannot accept a quote in status {Status}.");
        Status = QuoteStatus.Accepted;
    }

    public void Reject()
    {
        if (Status != QuoteStatus.Sent)
            throw new InvalidOperationException($"Cannot reject a quote in status {Status}.");
        Status = QuoteStatus.Rejected;
    }

    public void UpdateLineItems(string lineItemsJson, decimal totalValue)
    {
        if (Status != QuoteStatus.Draft)
            throw new InvalidOperationException("Can only update line items on a Draft quote.");
        LineItemsJson = lineItemsJson;
        TotalValue    = totalValue;
    }
}
