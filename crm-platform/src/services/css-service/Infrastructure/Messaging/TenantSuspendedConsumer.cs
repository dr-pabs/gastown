using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CrmPlatform.CssService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.CssService.Infrastructure.Messaging;

public sealed record TenantSuspendedMessage(
    Guid EventId,
    Guid TenantId,
    string SuspendedBy,
    DateTime OccurredAt);

/// <summary>
/// Consumes crm.platform / tenant.suspended.
/// Action: Soft-delete all open CSS cases — they become read-only.
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
    protected override string SubscriptionName => "css-service";

    protected override async Task<bool> ProcessAsync(
        TenantSuspendedMessage message,
        Guid tenantId,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CssDbContext>();

        var now = DateTime.UtcNow;

        var caseCount = await db.Cases
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == message.TenantId && !c.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.IsDeleted, true)
                .SetProperty(c => c.DeletedAt,  now), ct);

        logger.LogInformation(
            "Tenant {TenantId} suspended — marked {Cases} cases read-only",
            message.TenantId, caseCount);

        return true;
    }
}
