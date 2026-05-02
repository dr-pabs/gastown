using System.Text.Json;
using Azure.Messaging.ServiceBus;
using CrmPlatform.AnalyticsService.Domain.Entities;
using CrmPlatform.AnalyticsService.Domain.Enums;
using CrmPlatform.AnalyticsService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrmPlatform.AnalyticsService.Infrastructure.Messaging;

/// <summary>Generic envelope — we only need minimal fields to classify and store events.</summary>
public sealed record GenericEventEnvelope(
    Guid     EventId,
    Guid     TenantId,
    string   EventType,
    string?  EntityId,
    DateTime OccurredAt);

/// <summary>
/// Consumes crm.sfa topic — records every SFA domain event (lead, opp, quote)
/// into the analytics event log for later rollup.
/// </summary>
public sealed class SfaEventConsumer(
    ServiceBusClient serviceBusClient,
    IIdempotencyStore idempotencyStore,
    IOptions<ServiceBusConsumerOptions> options,
    ILogger<SfaEventConsumer> logger,
    IServiceScopeFactory scopeFactory)
    : BaseServiceBusConsumer<GenericEventEnvelope>(
        serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.sfa";
    protected override string SubscriptionName => "analytics-service";

    protected override async Task<bool> ProcessAsync(
        GenericEventEnvelope message,
        Guid tenantId,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        var evt = AnalyticsEvent.Record(
            message.TenantId,
            EventSource.Sfa,
            message.EventType,
            message.EntityId ?? string.Empty,
            JsonSerializer.Serialize(message),
            message.OccurredAt);

        db.Events.Add(evt);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Analytics: recorded SFA event {EventType} for tenant {TenantId}",
            message.EventType, message.TenantId);

        return true;
    }
}

/// <summary>Consumes crm.css topic — records every CSS domain event.</summary>
public sealed class CssEventConsumer(
    ServiceBusClient serviceBusClient,
    IIdempotencyStore idempotencyStore,
    IOptions<ServiceBusConsumerOptions> options,
    ILogger<CssEventConsumer> logger,
    IServiceScopeFactory scopeFactory)
    : BaseServiceBusConsumer<GenericEventEnvelope>(
        serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.css";
    protected override string SubscriptionName => "analytics-service";

    protected override async Task<bool> ProcessAsync(
        GenericEventEnvelope message,
        Guid tenantId,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        var evt = AnalyticsEvent.Record(
            message.TenantId,
            EventSource.Css,
            message.EventType,
            message.EntityId ?? string.Empty,
            JsonSerializer.Serialize(message),
            message.OccurredAt);

        db.Events.Add(evt);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Analytics: recorded CSS event {EventType} for tenant {TenantId}",
            message.EventType, message.TenantId);

        return true;
    }
}

/// <summary>Consumes crm.marketing topic — records every Marketing domain event.</summary>
public sealed class MarketingEventConsumer(
    ServiceBusClient serviceBusClient,
    IIdempotencyStore idempotencyStore,
    IOptions<ServiceBusConsumerOptions> options,
    ILogger<MarketingEventConsumer> logger,
    IServiceScopeFactory scopeFactory)
    : BaseServiceBusConsumer<GenericEventEnvelope>(
        serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.marketing";
    protected override string SubscriptionName => "analytics-service";

    protected override async Task<bool> ProcessAsync(
        GenericEventEnvelope message,
        Guid tenantId,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        var evt = AnalyticsEvent.Record(
            message.TenantId,
            EventSource.Marketing,
            message.EventType,
            message.EntityId ?? string.Empty,
            JsonSerializer.Serialize(message),
            message.OccurredAt);

        db.Events.Add(evt);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Analytics: recorded Marketing event {EventType} for tenant {TenantId}",
            message.EventType, message.TenantId);

        return true;
    }
}

/// <summary>Consumes crm.platform topic — records tenant lifecycle events.</summary>
public sealed class PlatformEventConsumer(
    ServiceBusClient serviceBusClient,
    IIdempotencyStore idempotencyStore,
    IOptions<ServiceBusConsumerOptions> options,
    ILogger<PlatformEventConsumer> logger,
    IServiceScopeFactory scopeFactory)
    : BaseServiceBusConsumer<GenericEventEnvelope>(
        serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.platform";
    protected override string SubscriptionName => "analytics-service";

    protected override async Task<bool> ProcessAsync(
        GenericEventEnvelope message,
        Guid tenantId,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        var evt = AnalyticsEvent.Record(
            message.TenantId,
            EventSource.Platform,
            message.EventType,
            message.EntityId ?? string.Empty,
            JsonSerializer.Serialize(message),
            message.OccurredAt);

        db.Events.Add(evt);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Analytics: recorded Platform event {EventType} for tenant {TenantId}",
            message.EventType, message.TenantId);

        return true;
    }
}
