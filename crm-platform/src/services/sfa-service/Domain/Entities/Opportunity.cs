using CrmPlatform.SfaService.Domain.Enums;
using CrmPlatform.SfaService.Domain.Events;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.SfaService.Domain.Entities;

/// <summary>
/// A sales opportunity in the pipeline.
/// Stage must advance sequentially: Qualify → Propose → Negotiate → Won | Lost.
/// </summary>
public sealed class Opportunity : BaseEntity
{
    private Opportunity() { } // EF Core

    public string           Title              { get; private set; } = string.Empty;
    public OpportunityStage Stage              { get; private set; } = OpportunityStage.Qualify;
    public decimal          Value              { get; private set; }
    public DateTime?        CloseDate          { get; private set; }
    public Guid?            ContactId          { get; private set; }
    public Guid?            AccountId          { get; private set; }
    public Guid?            AssignedToUserId   { get; private set; }
    public Guid?            ConvertedFromLeadId { get; private set; }

    // Navigation
    public Contact?             Contact    { get; private set; }
    public Account?             Account    { get; private set; }
    public IReadOnlyList<Quote> Quotes     { get; private set; } = [];
    // Activities are queried directly by RelatedEntityId + RelatedEntityType = "Opportunity"
    // No EF navigation — Activity is a polymorphic entity shared across Lead/Opportunity/Contact.

    private static readonly OpportunityStage[] TerminalStages = [OpportunityStage.Won, OpportunityStage.Lost];

    public static Opportunity Create(
        Guid tenantId,
        string title,
        decimal value,
        Guid? contactId,
        Guid? accountId,
        Guid? assignedToUserId,
        Guid? convertedFromLeadId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Value must be non-negative.");

        return new Opportunity
        {
            TenantId             = tenantId,
            Title                = title,
            Stage                = OpportunityStage.Qualify,
            Value                = value,
            ContactId            = contactId,
            AccountId            = accountId,
            AssignedToUserId     = assignedToUserId,
            ConvertedFromLeadId  = convertedFromLeadId,
        };
    }

    /// <summary>
    /// Advances the stage by exactly one step, or to Won/Lost from any non-terminal stage.
    /// Skipping stages (e.g. Qualify → Negotiate) is not permitted.
    /// </summary>
    public void AdvanceStage(OpportunityStage nextStage)
    {
        if (TerminalStages.Contains(Stage))
            throw new InvalidOperationException($"Opportunity is already in terminal stage {Stage}.");

        var isTerminalTarget = nextStage is OpportunityStage.Won or OpportunityStage.Lost;
        var isSequential     = (int)nextStage == (int)Stage + 1;

        if (!isTerminalTarget && !isSequential)
            throw new InvalidOperationException(
                $"Stage must advance sequentially. Cannot move from {Stage} to {nextStage}.");

        var previous = Stage;
        Stage = nextStage;

        if (nextStage == OpportunityStage.Won)
            AddDomainEvent(new OpportunityWonEvent(Id, TenantId, Value, AccountId));
        else if (nextStage == OpportunityStage.Lost)
            AddDomainEvent(new OpportunityLostEvent(Id, TenantId));
        else
            AddDomainEvent(new OpportunityStageChangedEvent(Id, TenantId, previous.ToString(), nextStage.ToString()));
    }

    public void UpdateDetails(string? title, decimal? value, DateTime? closeDate)
    {
        if (!string.IsNullOrWhiteSpace(title)) Title = title;
        if (value.HasValue)
        {
            if (value.Value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            Value = value.Value;
        }
        if (closeDate.HasValue) CloseDate = closeDate;
    }
}
