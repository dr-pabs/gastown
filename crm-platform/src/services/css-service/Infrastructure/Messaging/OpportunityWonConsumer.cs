using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CrmPlatform.CssService.Domain.Entities;
using CrmPlatform.CssService.Domain.Enums;
using CrmPlatform.CssService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.CssService.Infrastructure.Messaging;

public sealed record OpportunityWonMessage(
    Guid    EventId,
    Guid    TenantId,
    Guid    OpportunityId,
    decimal Value,
    Guid?   AccountId,
    DateTime OccurredAt);

/// <summary>
/// Consumes crm.sfa / opportunity.won.
/// Action: Auto-create an onboarding support case for the winning opportunity's account.
/// Only fires if the tenant has a SlaPolicy configured (signals CSS is in use).
/// </summary>
public sealed class OpportunityWonConsumer(
    ServiceBusClient serviceBusClient,
    IIdempotencyStore idempotencyStore,
    IOptions<ServiceBusConsumerOptions> options,
    ILogger<OpportunityWonConsumer> logger,
    IServiceScopeFactory scopeFactory)
    : BaseServiceBusConsumer<OpportunityWonMessage>(
        serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.sfa";
    protected override string SubscriptionName => "css-service";

    protected override async Task<bool> ProcessAsync(
        OpportunityWonMessage message,
        Guid tenantId,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CssDbContext>();

        // Only auto-create if the tenant has any SLA policy configured
        var hasSlaPolicy = await db.SlaPolicies
            .IgnoreQueryFilters()
            .AnyAsync(p => p.TenantId == message.TenantId, ct);

        if (!hasSlaPolicy)
        {
            logger.LogInformation(
                "OpportunityWon: Tenant {TenantId} has no SLA policies — skipping onboarding case",
                message.TenantId);
            return true;
        }

        var onboardingCase = Case.Create(
            tenantId:       message.TenantId,
            title:          $"Onboarding — Opportunity {message.OpportunityId}",
            description:    $"Auto-created onboarding case for won opportunity {message.OpportunityId} (£{message.Value:N2}).",
            priority:       CasePriority.Medium,
            channel:        CaseChannel.Api,
            contactId:      null,
            accountId:      message.AccountId,
            createdByUserId: message.TenantId); // system actor — use TenantId as sentinel

        db.Cases.Add(onboardingCase);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "OpportunityWon: Created onboarding case {CaseId} for opportunity {OpportunityId}",
            onboardingCase.Id, message.OpportunityId);

        return true;
    }
}
