using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace CrmPlatform.AiOrchestrationService.Infrastructure.Teams;

public interface ITeamsNotificationClient
{
    Task SendAdaptiveCardAsync(
        string webhookUrl,
        string cardTitle,
        string cardBody,
        CancellationToken ct = default);
}

/// <summary>
/// Sends Adaptive Cards to Teams channels via incoming webhook URLs.
/// Webhook URL stored per-tenant in Key Vault: teams-webhook-{tenantId}.
/// </summary>
public sealed class TeamsNotificationClient(
    HttpClient                        httpClient,
    ILogger<TeamsNotificationClient>  logger)
    : ITeamsNotificationClient
{
    public async Task SendAdaptiveCardAsync(
        string            webhookUrl,
        string            cardTitle,
        string            cardBody,
        CancellationToken ct = default)
    {
        // Compose an Adaptive Card payload (Teams MessageCard-compatible)
        var payload = new
        {
            type    = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    contentUrl  = (string?)null,
                    content     = new
                    {
                        type    = "AdaptiveCard",
                        version = "1.4",
                        body    = new object[]
                        {
                            new { type = "TextBlock", size = "Medium", weight = "Bolder", text = cardTitle },
                            new { type = "TextBlock", wrap = true, text = cardBody }
                        }
                    }
                }
            }
        };

        logger.LogInformation("Sending Teams Adaptive Card to webhook");

        var response = await httpClient.PostAsJsonAsync(webhookUrl, payload, ct);
        response.EnsureSuccessStatusCode();

        logger.LogInformation("Teams Adaptive Card sent successfully");
    }
}
