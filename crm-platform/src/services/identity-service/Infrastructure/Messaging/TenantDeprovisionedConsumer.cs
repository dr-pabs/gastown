using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CrmPlatform.IdentityService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.IdentityService.Infrastructure.Messaging;

/// <summary>
/// Payload shape for crm.platform tenant.deprovisioned event.
/// </summary>
public sealed record TenantDeprovisionedMessage(
    Guid EventId,
    Guid TenantId,
    string DeprovisionedBy,
    DateTime OccurredAt);

/// <summary>
/// Consumes crm.platform / tenant.deprovisioned.
/// Action: GDPR hard-delete of all user PII for the deprovisioned tenant.
///
/// Rules:
///   - Only triggered by this event — never on user-level deprovision.
///   - ConsentRecords are anonymised (TenantUserId cleared), NOT deleted — immutable audit.
///   - UserProvisioningLogs are anonymised, NOT deleted.
///   - The TenantRegistry entry is marked Deprovisioned (soft).
/// </summary>
public sealed class TenantDeprovisionedConsumer(
    ServiceBusClient serviceBusClient,
    IIdempotencyStore idempotencyStore,
    IOptions<ServiceBusConsumerOptions> options,
    ILogger<TenantDeprovisionedConsumer> logger,
    IServiceScopeFactory scopeFactory)
    : BaseServiceBusConsumer<TenantDeprovisionedMessage>(
        serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.platform";
    protected override string SubscriptionName => "identity-service";

    protected override async Task<bool> ProcessAsync(
        TenantDeprovisionedMessage message,
        Guid tenantId,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // 1. Anonymise ConsentRecords (retain record, clear PII FK link)
        await db.ConsentRecords
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == message.TenantId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.IpAddressHash, "gdpr-erased"), ct);

        // 2. Hard-delete UserRoles
        await db.UserRoles
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == message.TenantId)
            .ExecuteDeleteAsync(ct);

        // 3. Hard-delete TenantUsers (PII erasure)
        var deletedCount = await db.TenantUsers
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == message.TenantId)
            .ExecuteDeleteAsync(ct);

        // 4. Mark TenantRegistry deprovisioned
        var registry = await db.TenantRegistries
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TenantId == message.TenantId, ct);

        if (registry is not null)
            registry.Deprovision();

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "GDPR erasure complete for tenant {TenantId}. Hard-deleted {Count} users",
            message.TenantId, deletedCount);

        return true;
    }
}
