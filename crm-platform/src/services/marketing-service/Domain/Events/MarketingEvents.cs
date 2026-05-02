using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.MarketingService.Domain.Events;

/// <summary>All domain events published by marketing-service to crm.marketing topic.</summary>

public record CampaignCreatedEvent(
    Guid CampaignId,
    Guid TenantId,
    Guid CreatedByUserId) : DomainEvent(TenantId)
{
    public override string EventType => "campaign.created";
}

public record CampaignActivatedEvent(
    Guid CampaignId,
    Guid TenantId) : DomainEvent(TenantId)
{
    public override string EventType => "campaign.activated";
}

public record JourneyPublishedEvent(
    Guid CampaignId,
    Guid TenantId,
    Guid JourneyId,
    int  StepCount) : DomainEvent(TenantId)
{
    public override string EventType => "journey.published";
}

/// <summary>
/// Published when all journey steps have been completed for a contact.
/// Consumed by sfa-service JourneyCompletedConsumer → qualifies the lead.
/// </summary>
public record JourneyCompletedEvent(
    Guid EnrollmentId,
    Guid TenantId,
    Guid JourneyId,
    Guid ContactId,
    DateTime OccurredAt) : DomainEvent(TenantId)
{
    public override string EventType => "journey.completed";
}

public record JourneyEnrollmentCreatedEvent(
    Guid EnrollmentId,
    Guid TenantId,
    Guid JourneyId,
    Guid ContactId) : DomainEvent(TenantId)
{
    public override string EventType => "journey.enrollment.created";
}

public record JourneyEnrollmentExitedEvent(
    Guid   EnrollmentId,
    Guid   TenantId,
    Guid   JourneyId,
    string Reason) : DomainEvent(TenantId)
{
    public override string EventType => "journey.enrollment.exited";
}
