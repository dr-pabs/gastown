using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CrmPlatform.IdentityService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.IdentityService.Infrastructure.Messaging;

/// <summary>
/// Payload shape for crm.platform tenant.suspended event.
/// </summary>
public sealed record TenantSuspendedMessage(
    Guid EventId,
    Guid TenantId,
    string SuspendedBy,
    DateTime OccurredAt);

/// <summary>
/// Consumes crm.platform / tenant.suspended.
/// Action: Soft-delete (suspend) all TenantUsers belonging to the tenant.
/// This effectively revokes all sessions on next token validation.
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
    protected override string SubscriptionName => "identity-service";

    protected override async Task<bool> ProcessAsync(
        TenantSuspendedMessage message,
        Guid tenantId,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // Use IgnoreQueryFilters — we need all users including soft-deleted for this tenant
        var users = await db.TenantUsers
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == message.TenantId && !u.IsDeleted)
            .ToListAsync(ct);

        foreach (var user in users)
            user.Suspend();

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Suspended {Count} users for tenant {TenantId} due to tenant.suspended event",
            users.Count, message.TenantId);

        return true;
    }
}
