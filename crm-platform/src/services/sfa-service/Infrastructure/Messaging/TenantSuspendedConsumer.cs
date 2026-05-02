using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CrmPlatform.SfaService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.SfaService.Infrastructure.Messaging;

public sealed record TenantSuspendedMessage(
    Guid EventId,
    Guid TenantId,
    string SuspendedBy,
    DateTime OccurredAt);

/// <summary>
/// Consumes crm.platform / tenant.suspended.
/// Action: Soft-delete all SFA records for the suspended tenant so they become read-only.
/// Each entity type is soft-deleted in bulk — no hard deletes.
/// </summary>
public sealed class TenantSuspendedConsumer(
    ServiceBusClient serviceBusClient,
    IIdempotencyStore idempotencyStore,
    IOptions<ServiceBusConsumerOptions> options,
    ILogger<TenantSuspendedConsumer> logger,
    IServiceScopeFactory scopeFactory)
    : BaseServiceBusConsumer<TenantSuspendedMessage>(
        serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.platform";
    protected override string SubscriptionName => "sfa-service";

    protected override async Task<bool> ProcessAsync(
        TenantSuspendedMessage message,
        Guid tenantId,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfaDbContext>();

        var now = DateTime.UtcNow;

        // Bulk-mark leads suspended (IsDeleted = true acts as "suspended" gate)
        var leadCount = await db.Leads
            .IgnoreQueryFilters()
            .Where(l => l.TenantId == message.TenantId && !l.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.IsDeleted, true), ct);

        var oppCount = await db.Opportunities
            .IgnoreQueryFilters()
            .Where(o => o.TenantId == message.TenantId && !o.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.IsDeleted, true), ct);

        logger.LogInformation(
            "Tenant {TenantId} suspended — marked {Leads} leads and {Opps} opportunities read-only",
            message.TenantId, leadCount, oppCount);

        return true;
    }
}
