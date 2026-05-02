using CrmPlatform.IntegrationService.Domain.Entities;
using CrmPlatform.IntegrationService.Domain.Enums;
using CrmPlatform.IntegrationService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CrmPlatform.ServiceTemplate.Infrastructure.Data;

namespace CrmPlatform.IntegrationService.Infrastructure.Messaging;

// ─── Consumed message shapes (file-scoped) ────────────────────────────────────

public sealed record TenantProvisionedMessage(Guid TenantId, string Slug, string AdminEmail);
public sealed record TenantSuspendedMessage(Guid TenantId, string Reason);
public sealed record LeadAssignedMessage(Guid TenantId, Guid LeadId, Guid AssignedToUserId, string LeadName);
public sealed record OpportunityWonMessage(Guid TenantId, Guid OpportunityId, string Title, decimal Amount);
public sealed record CaseCreatedMessage(Guid TenantId, Guid CaseId, string CaseNumber, string Title);
public sealed record CaseResolvedMessage(Guid TenantId, Guid CaseId, string CaseNumber);

// ─── Consumers ────────────────────────────────────────────────────────────────

/// <summary>
/// TenantProvisioned — log only; no connector setup needed at provisioning time.
/// </summary>
public sealed class IntTenantProvisionedConsumer(
    Azure.Messaging.ServiceBus.ServiceBusClient serviceBusClient,
    IIdempotencyStore idempotencyStore,
    Microsoft.Extensions.Options.IOptions<ServiceBusConsumerOptions> options,
    ILogger<IntTenantProvisionedConsumer> logger)
    : BaseServiceBusConsumer<TenantProvisionedMessage>(serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.platform";
    protected override string SubscriptionName => "integration-service";

    protected override Task<bool> ProcessAsync(TenantProvisionedMessage msg, Guid tenantId, CancellationToken ct)
    {
        logger.LogInformation("Tenant {TenantId} provisioned — no integration setup required", tenantId);
        return Task.FromResult(true);
    }
}

/// <summary>
/// TenantSuspended — suspends all connector configs for the tenant.
/// </summary>
public sealed class IntTenantSuspendedConsumer(
    Azure.Messaging.ServiceBus.ServiceBusClient serviceBusClient,
    IIdempotencyStore idempotencyStore,
    Microsoft.Extensions.Options.IOptions<ServiceBusConsumerOptions> options,
    ILogger<IntTenantSuspendedConsumer> logger,
    IServiceScopeFactory scopeFactory)
    : BaseServiceBusConsumer<TenantSuspendedMessage>(serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.platform";
    protected override string SubscriptionName => "integration-service-suspended";

    protected override async Task<bool> ProcessAsync(TenantSuspendedMessage msg, Guid tenantId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();

        var connectors = await db.Connectors
            .Where(c => c.Status != ConnectorStatus.Suspended)
            .ToListAsync(ct);

        foreach (var c in connectors) c.Suspend();

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Suspended {Count} connector(s) for tenant {TenantId}", connectors.Count, tenantId);
        return true;
    }
}

/// <summary>
/// LeadAssigned — enqueue outbound job for all connected SFA-capable connectors.
/// </summary>
public sealed class LeadAssignedConsumer(
    Azure.Messaging.ServiceBus.ServiceBusClient serviceBusClient,
    IIdempotencyStore idempotencyStore,
    Microsoft.Extensions.Options.IOptions<ServiceBusConsumerOptions> options,
    ILogger<LeadAssignedConsumer> logger,
    IServiceScopeFactory scopeFactory)
    : BaseServiceBusConsumer<LeadAssignedMessage>(serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.sfa";
    protected override string SubscriptionName => "integration-service";

    protected override async Task<bool> ProcessAsync(LeadAssignedMessage msg, Guid tenantId, CancellationToken ct)
    {
        await ConsumerHelpers.EnqueueOutboundJobsAsync(tenantId, "lead.assigned",
            System.Text.Json.JsonSerializer.Serialize(msg), scopeFactory, logger, ct);
        return true;
    }
}

/// <summary>
/// OpportunityWon — enqueue outbound job for connected SFA connectors.
/// </summary>
public sealed class OpportunityWonConsumer(
    Azure.Messaging.ServiceBus.ServiceBusClient serviceBusClient,
    IIdempotencyStore idempotencyStore,
    Microsoft.Extensions.Options.IOptions<ServiceBusConsumerOptions> options,
    ILogger<OpportunityWonConsumer> logger,
    IServiceScopeFactory scopeFactory)
    : BaseServiceBusConsumer<OpportunityWonMessage>(serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.sfa";
    protected override string SubscriptionName => "integration-service-won";

    protected override async Task<bool> ProcessAsync(OpportunityWonMessage msg, Guid tenantId, CancellationToken ct)
    {
        await ConsumerHelpers.EnqueueOutboundJobsAsync(tenantId, "opportunity.won",
            System.Text.Json.JsonSerializer.Serialize(msg), scopeFactory, logger, ct);
        return true;
    }
}

/// <summary>
/// CaseCreated — enqueue outbound job for connected CSS connectors.
/// </summary>
public sealed class CaseCreatedIntConsumer(
    Azure.Messaging.ServiceBus.ServiceBusClient serviceBusClient,
    IIdempotencyStore idempotencyStore,
    Microsoft.Extensions.Options.IOptions<ServiceBusConsumerOptions> options,
    ILogger<CaseCreatedIntConsumer> logger,
    IServiceScopeFactory scopeFactory)
    : BaseServiceBusConsumer<CaseCreatedMessage>(serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.css";
    protected override string SubscriptionName => "integration-service";

    protected override async Task<bool> ProcessAsync(CaseCreatedMessage msg, Guid tenantId, CancellationToken ct)
    {
        await ConsumerHelpers.EnqueueOutboundJobsAsync(tenantId, "case.created",
            System.Text.Json.JsonSerializer.Serialize(msg), scopeFactory, logger, ct);
        return true;
    }
}

/// <summary>
/// CaseResolved — enqueue outbound job for connected CSS connectors.
/// </summary>
public sealed class CaseResolvedConsumer(
    Azure.Messaging.ServiceBus.ServiceBusClient serviceBusClient,
    IIdempotencyStore idempotencyStore,
    Microsoft.Extensions.Options.IOptions<ServiceBusConsumerOptions> options,
    ILogger<CaseResolvedConsumer> logger,
    IServiceScopeFactory scopeFactory)
    : BaseServiceBusConsumer<CaseResolvedMessage>(serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.css";
    protected override string SubscriptionName => "integration-service-resolved";

    protected override async Task<bool> ProcessAsync(CaseResolvedMessage msg, Guid tenantId, CancellationToken ct)
    {
        await ConsumerHelpers.EnqueueOutboundJobsAsync(tenantId, "case.resolved",
            System.Text.Json.JsonSerializer.Serialize(msg), scopeFactory, logger, ct);
        return true;
    }
}

// ─── Shared helper ────────────────────────────────────────────────────────────

file static class ConsumerHelpers
{
    /// <summary>
    /// Enqueues an OutboundJob for every Connected connector of a relevant type for the tenant.
    /// AzureEventHub connectors are also included (real-time streaming of all CRM events).
    /// AzureBlobExport connectors are excluded (handled by BlobExportWorker, not per-event).
    /// </summary>
    internal static async Task EnqueueOutboundJobsAsync(
        Guid tenantId,
        string eventType,
        string payload,
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();

        var connectors = await db.Connectors
            .Where(c => c.Status == ConnectorStatus.Connected
                     && c.ConnectorType != ConnectorType.AzureBlobExport)
            .ToListAsync(ct);

        if (connectors.Count == 0) return;

        foreach (var connector in connectors)
        {
            var job = OutboundJob.Create(tenantId, connector.Id, connector.ConnectorType, eventType, payload);
            db.OutboundJobs.Add(job);
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Enqueued {Count} outbound job(s) for event {EventType} tenant {TenantId}",
            connectors.Count, eventType, tenantId);
    }
}
