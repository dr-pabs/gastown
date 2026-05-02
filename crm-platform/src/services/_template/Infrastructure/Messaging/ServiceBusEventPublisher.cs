using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

/// <summary>
/// Publishes domain events as Service Bus messages.
/// Injected as scoped — collects domain events during the request,
/// then PublishPendingAsync() is called after SaveChanges succeeds.
///
/// Rules:
///   - Always include tenantId in ApplicationProperties.
///   - Always include eventType in ApplicationProperties (for subscription filters).
///   - Use MessageId = EventId.ToString() to enable Service Bus deduplication.
/// </summary>
public sealed class ServiceBusEventPublisher(
    ServiceBusClient serviceBusClient,
    ILogger<ServiceBusEventPublisher> logger)
{
    private readonly List<(string Topic, IDomainEvent Event)> _pending = [];

    public void Enqueue(string topicName, IDomainEvent domainEvent) =>
        _pending.Add((topicName, domainEvent));

    public async Task PublishAsync(string topicName, IDomainEvent domainEvent, CancellationToken ct = default)
    {
        await using var sender = serviceBusClient.CreateSender(topicName);
        var body = JsonSerializer.SerializeToUtf8Bytes(domainEvent);
        var message = new ServiceBusMessage(body)
        {
            MessageId = domainEvent.EventId.ToString(),
            ContentType = "application/json",
            Subject = domainEvent.EventType,
            ApplicationProperties =
            {
                ["tenantId"] = domainEvent.TenantId.ToString(),
                ["eventType"] = domainEvent.EventType,
                ["occurredAt"] = domainEvent.OccurredAt.ToString("O"),
            },
        };

        await sender.SendMessageAsync(message, ct);
        logger.LogInformation("Published {EventType} (EventId={EventId}) to {Topic}",
            domainEvent.EventType, domainEvent.EventId, topicName);
    }

    public async Task PublishPendingAsync(CancellationToken ct = default)
    {
        if (_pending.Count == 0) return;

        foreach (var (topic, domainEvent) in _pending)
        {
            try
            {
                await PublishAsync(topic, domainEvent, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish {EventType} to {Topic}", domainEvent.EventType, topic);
                throw; // Bubble up — caller (UoW) should handle and potentially retry
            }
        }

        _pending.Clear();
    }
}
