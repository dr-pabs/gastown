using Azure.Communication.CallingServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CrmPlatform.AiOrchestrationService.Infrastructure.Teams;

public sealed record CallInitiationResult(
    string CallId,
    bool   IsConnected);

public interface ITeamsCallingClient
{
    Task<CallInitiationResult> InitiateCallAsync(
        string            targetAcsUserId,
        string            callbackUri,
        CancellationToken ct = default);
}

/// <summary>
/// Initiates outbound Teams calls via Azure Communication Services Calling SDK.
/// Transcript retrieval is handled by a separate ACS webhook callback (not in scope here).
/// </summary>
public sealed class TeamsCallingClient(
    IConfiguration              config,
    ILogger<TeamsCallingClient> logger)
    : ITeamsCallingClient
{
    private CallAutomationClient BuildClient() =>
        new(config["Azure:CommunicationServices:ConnectionString"]!);

    public async Task<CallInitiationResult> InitiateCallAsync(
        string            targetAcsUserId,
        string            callbackUri,
        CancellationToken ct = default)
    {
        logger.LogInformation("Initiating ACS call to={TargetUser}", targetAcsUserId);

        var client  = BuildClient();
        var target  = new CommunicationUserIdentifier(targetAcsUserId);
        var source  = new CommunicationUserIdentifier(
                          config["Azure:CommunicationServices:BotUserId"]!);

        var result = await client.CreateCallAsync(
            new CreateCallOptions(
                new CallInvite(target),
                new Uri(callbackUri)),
            ct);

        var callId = result.Value.CallConnectionProperties.CallConnectionId;

        logger.LogInformation("ACS call created callId={CallId}", callId);

        return new CallInitiationResult(CallId: callId, IsConnected: true);
    }
}
