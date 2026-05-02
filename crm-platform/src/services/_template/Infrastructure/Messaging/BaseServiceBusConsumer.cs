using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

/// <summary>
/// Base Service Bus consumer. All service consumers inherit from this.
///
/// Rules (from ADR 0002 + CLAUDE.md):
///   - Always check the idempotency store before processing. If MessageId seen → abandon/complete.
///   - Use dead-letter for poison messages (max 3 delivery attempts before DLQ).
///   - Never call another service over HTTP inside a handler — publish a follow-up event instead.
///   - Structured log every message with MessageId + TenantId.
/// </summary>
public abstract class BaseServiceBusConsumer<TMessage>(
    ServiceBusClient serviceBusClient,
    IIdempotencyStore idempotencyStore,
    IOptions<ServiceBusConsumerOptions> options,
    ILogger logger)
{
    private ServiceBusProcessor? _processor;

    protected abstract string TopicName { get; }
    protected abstract string SubscriptionName { get; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _processor = serviceBusClient.CreateProcessor(
            TopicName,
            SubscriptionName,
            new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = options.Value.MaxConcurrentCalls,
                AutoCompleteMessages = false,
            });

        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        await _processor.StartProcessingAsync(cancellationToken);
        logger.LogInformation("Started consuming {Topic}/{Subscription}", TopicName, SubscriptionName);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }
    }

    // ─── Override in derived consumer ────────────────────────────────────────
    protected abstract Task<bool> ProcessAsync(TMessage message, Guid tenantId, CancellationToken ct);

    // ─── Internal plumbing ────────────────────────────────────────────────────
    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var messageId = args.Message.MessageId;
        var tenantId = Guid.Empty;

        if (args.Message.ApplicationProperties.TryGetValue("tenantId", out var tid))
            Guid.TryParse(tid?.ToString(), out tenantId);

        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["MessageId"] = messageId,
            ["TenantId"] = tenantId,
            ["Topic"] = TopicName,
        });

        // ─── Idempotency check ────────────────────────────────────────────
        if (await idempotencyStore.HasBeenProcessedAsync(messageId, args.CancellationToken))
        {
            logger.LogInformation("Duplicate message {MessageId} — completing without processing", messageId);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        try
        {
            var body = args.Message.Body.ToObjectFromJson<TMessage>();
            if (body is null) throw new InvalidOperationException("Message body deserialized to null");

            var handled = await ProcessAsync(body, tenantId, args.CancellationToken);

            if (handled)
            {
                await idempotencyStore.MarkProcessedAsync(messageId, args.CancellationToken);
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                logger.LogInformation("Processed message {MessageId}", messageId);
            }
            else
            {
                // Handler explicitly rejected — abandon for retry
                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
                logger.LogWarning("Handler rejected message {MessageId} — abandoned for retry", messageId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing message {MessageId} — will dead-letter after max delivery", messageId);
            // Do NOT dead-letter manually — let Service Bus retry policy + max delivery count handle it
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(args.Exception, "Service Bus processor error on {Topic}/{Subscription}: {Source}",
            TopicName, SubscriptionName, args.ErrorSource);
        return Task.CompletedTask;
    }
}

public sealed class ServiceBusConsumerOptions
{
    public int MaxConcurrentCalls { get; set; } = 4;
}

/// <summary>
/// Persists processed message IDs to prevent duplicate processing.
/// Implement using EF Core — store in the service's own DB schema (dbo.IdempotencyStore).
/// </summary>
public interface IIdempotencyStore
{
    Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken ct = default);
    Task MarkProcessedAsync(string messageId, CancellationToken ct = default);
}
