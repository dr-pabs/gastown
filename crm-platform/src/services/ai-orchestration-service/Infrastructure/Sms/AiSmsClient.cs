using Azure;
using Azure.Communication.Sms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CrmPlatform.AiOrchestrationService.Infrastructure.Sms;

public interface IAiSmsClient
{
    Task<string> SendAsync(
        string fromNumber,
        string toNumber,
        string message,
        CancellationToken ct = default);
}

/// <summary>
/// Sends AI-composed SMS messages via Azure Communication Services.
/// ai-orchestration-service owns its own ACS SMS client — does NOT delegate
/// to notification-service.
/// </summary>
public sealed class AiSmsClient(
    IConfiguration          config,
    ILogger<AiSmsClient>    logger)
    : IAiSmsClient
{
    private SmsClient BuildClient() =>
        new(config["Azure:CommunicationServices:ConnectionString"]!);

    public async Task<string> SendAsync(
        string            fromNumber,
        string            toNumber,
        string            message,
        CancellationToken ct = default)
    {
        logger.LogInformation("Sending AI-composed SMS to={To} from={From}", toNumber, fromNumber);

        var client  = BuildClient();
        var results = await client.SendAsync(
            from: fromNumber,
            to:   new[] { toNumber },
            message: message,
            cancellationToken: ct);

        foreach (var result in results.Value)
        {
            if (!result.Successful)
            {
                logger.LogWarning("ACS SMS failed for {To}: {Error}", result.To, result.ErrorMessage);
                throw new InvalidOperationException($"ACS SMS failed: {result.ErrorMessage}");
            }

            logger.LogInformation("ACS SMS sent messageId={MessageId} to={To}",
                result.MessageId, result.To);
            return result.MessageId;
        }

        throw new InvalidOperationException("ACS returned no SMS results.");
    }
}
