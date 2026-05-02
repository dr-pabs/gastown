namespace CrmPlatform.SfaService.Infrastructure.Messaging;

public sealed class ConsumerHostedService<TConsumer>(TConsumer consumer) : IHostedService
    where TConsumer : class
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var method = typeof(TConsumer).GetMethod("StartAsync")!;
        return (Task)method.Invoke(consumer, [cancellationToken])!;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        var method = typeof(TConsumer).GetMethod("StopAsync")!;
        return (Task)method.Invoke(consumer, [cancellationToken])!;
    }
}
