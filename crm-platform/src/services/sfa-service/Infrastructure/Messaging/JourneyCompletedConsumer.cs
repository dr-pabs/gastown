using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CrmPlatform.SfaService.Domain.Enums;
using CrmPlatform.SfaService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.SfaService.Infrastructure.Messaging;

public sealed record JourneyCompletedMessage(
    Guid EventId,
    Guid TenantId,
    Guid LeadId,
    Guid JourneyId,
    string JourneyName,
    DateTime OccurredAt);

/// <summary>
/// Consumes crm.marketing / journey.completed.
/// Action: Advance a lead to Qualified status when a marketing journey is completed
/// and the lead is still in an early stage (New or Contacted).
/// </summary>
public sealed class JourneyCompletedConsumer(
    ServiceBusClient serviceBusClient,
    IIdempotencyStore idempotencyStore,
    IOptions<ServiceBusConsumerOptions> options,
    ILogger<JourneyCompletedConsumer> logger,
    IServiceScopeFactory scopeFactory)
    : BaseServiceBusConsumer<JourneyCompletedMessage>(
        serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.marketing";
    protected override string SubscriptionName => "sfa-service";

    protected override async Task<bool> ProcessAsync(
        JourneyCompletedMessage message,
        Guid tenantId,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfaDbContext>();

        var lead = await db.Leads
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                l => l.Id == message.LeadId && l.TenantId == message.TenantId,
                ct);

        if (lead is null)
        {
            logger.LogWarning(
                "JourneyCompleted: Lead {LeadId} not found for tenant {TenantId}",
                message.LeadId, message.TenantId);
            // Ack the message — lead may have been deleted; not a transient error.
            return true;
        }

        if (lead.Status is not (LeadStatus.New or LeadStatus.Contacted))
        {
            logger.LogInformation(
                "JourneyCompleted: Lead {LeadId} already in status {Status}, no action taken",
                lead.Id, lead.Status);
            return true;
        }

        lead.Qualify();

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "JourneyCompleted: Lead {LeadId} qualified via journey {JourneyId} ({JourneyName})",
            lead.Id, message.JourneyId, message.JourneyName);

        return true;
    }
}
