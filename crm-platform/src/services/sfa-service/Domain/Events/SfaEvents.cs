using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.SfaService.Domain.Events;

/// <summary>All domain events published by sfa-service to crm.sfa topic.</summary>

public record LeadCreatedEvent(
    Guid LeadId,
    Guid TenantId,
    Guid CreatedByUserId) : DomainEvent(TenantId)
{
    public override string EventType => "lead.created";
}

public record LeadAssignedEvent(
    Guid LeadId,
    Guid TenantId,
    Guid AssignedToUserId,
    Guid AssignedByUserId) : DomainEvent(TenantId)
{
    public override string EventType => "lead.assigned";
}

public record LeadConvertedEvent(
    Guid LeadId,
    Guid TenantId,
    Guid OpportunityId) : DomainEvent(TenantId)
{
    public override string EventType => "lead.converted";
}

public record OpportunityStageChangedEvent(
    Guid   OpportunityId,
    Guid   TenantId,
    string PreviousStage,
    string NewStage) : DomainEvent(TenantId)
{
    public override string EventType => "opportunity.stage.changed";
}

public record OpportunityWonEvent(
    Guid    OpportunityId,
    Guid    TenantId,
    decimal Value,
    Guid?   AccountId) : DomainEvent(TenantId)
{
    public override string EventType => "opportunity.won";
}

public record OpportunityLostEvent(
    Guid OpportunityId,
    Guid TenantId) : DomainEvent(TenantId)
{
    public override string EventType => "opportunity.lost";
}

public record QuoteSentEvent(
    Guid QuoteId,
    Guid TenantId,
    Guid OpportunityId) : DomainEvent(TenantId)
{
    public override string EventType => "quote.sent";
}
