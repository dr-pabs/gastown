using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CrmPlatform.MarketingService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.MarketingService.Infrastructure.Messaging;

public sealed record TenantSuspendedMessage(
    Guid EventId,
    Guid TenantId,
    DateTime OccurredAt);

/// <summary>
/// Consumes crm.platform / tenant.suspended.
/// Action: Soft-delete all Campaigns, Journeys, and Enrollments for the tenant.
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
    protected override string SubscriptionName => "marketing-service";

    protected override async Task<bool> ProcessAsync(
        TenantSuspendedMessage message,
        Guid tenantId,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketingDbContext>();

        var now = DateTime.UtcNow;

        // Bulk soft-delete campaigns
        await db.Campaigns
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == message.TenantId && !c.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.IsDeleted,  true)
                .SetProperty(c => c.DeletedAt,  now),
                ct);

        // Bulk soft-delete journeys
        await db.Journeys
            .IgnoreQueryFilters()
            .Where(j => j.TenantId == message.TenantId && !j.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.IsDeleted, true)
                .SetProperty(j => j.DeletedAt, now),
                ct);

        // Bulk soft-delete enrollments
        await db.Enrollments
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == message.TenantId && !e.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.IsDeleted, true)
                .SetProperty(e => e.DeletedAt, now),
                ct);

        logger.LogWarning(
            "TenantSuspended: bulk soft-deleted marketing data for tenant {TenantId}",
            message.TenantId);

        return true;
    }
}
