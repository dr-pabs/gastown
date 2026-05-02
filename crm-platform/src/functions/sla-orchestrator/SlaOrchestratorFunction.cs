using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace CrmPlatform.Functions.SlaOrchestrator;

// ──────────────────────────────────────────────────────────────────────────────
// Messages
// ──────────────────────────────────────────────────────────────────────────────
public sealed record CaseOpenedMessage(
    Guid   CaseId,
    Guid   TenantId,
    string CaseNumber,
    DateTime SlaDeadline);

public sealed record SlaInput(
    Guid     CaseId,
    Guid     TenantId,
    string   CaseNumber,
    DateTime SlaDeadline);

public sealed record CaseActivityInput(
    Guid   CaseId,
    Guid   TenantId);

// ──────────────────────────────────────────────────────────────────────────────
// Function class
// ──────────────────────────────────────────────────────────────────────────────
public class SlaOrchestratorFunction(IHttpClientFactory httpClientFactory, ILogger<SlaOrchestratorFunction> logger)
{
    // ─── Trigger ──────────────────────────────────────────────────────────────
    [Function(nameof(SlaOrchestratorTrigger))]
    public async Task SlaOrchestratorTrigger(
        [ServiceBusTrigger("crm.css", "sla-orchestrator", Connection = "ServiceBus")] ServiceBusReceivedMessage serviceBusMessage,
        [DurableClient] DurableTaskClient client)
    {
        var message = JsonSerializer.Deserialize<CaseOpenedMessage>(serviceBusMessage.Body.ToString())
            ?? throw new InvalidOperationException("SLA trigger payload is required.");
        var instanceId = $"sla-{message.CaseId}";

        // Idempotency — don't re-start if already running
        var existing = await client.GetInstanceAsync(instanceId);
        if (existing is not null &&
            (existing.RuntimeStatus == OrchestrationRuntimeStatus.Running ||
             existing.RuntimeStatus == OrchestrationRuntimeStatus.Pending))
        {
            logger.LogInformation(
                "SLA orchestration {InstanceId} already running for case {CaseId}. Skipping.",
                instanceId, message.CaseId);
            return;
        }

        var input = new SlaInput(message.CaseId, message.TenantId, message.CaseNumber, message.SlaDeadline);

        await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(SlaOrchestrator),
            input,
            new StartOrchestrationOptions { InstanceId = instanceId });

        logger.LogInformation(
            "Started SLA orchestration {InstanceId} for case {CaseNumber} (deadline {Deadline:u})",
            instanceId, message.CaseNumber, message.SlaDeadline);
    }

    // ─── Orchestrator ─────────────────────────────────────────────────────────
    [Function(nameof(SlaOrchestrator))]
    public static async Task SlaOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var log   = context.CreateReplaySafeLogger(nameof(SlaOrchestrator));
        var input = context.GetInput<SlaInput>()!;

        // Step 1 — store the InstanceId on the Case entity so css-service can
        //           raise the "cancel" event when the case is resolved/closed.
        await context.CallActivityAsync(
            nameof(StoreInstanceIdActivity),
            new CaseActivityInput(input.CaseId, input.TenantId));

        // ── Warning phase ─────────────────────────────────────────────────────
        // Warn at 80% of total SLA duration remaining.
        var now            = context.CurrentUtcDateTime;
        var totalDuration  = input.SlaDeadline - now;
        var warningOffset  = TimeSpan.FromTicks((long)(totalDuration.Ticks * 0.80));
        var warningDeadline = now + warningOffset;

        if (warningDeadline < input.SlaDeadline)
        {
            log.LogInformation(
                "SLA warning scheduled at {Warning:u} for case {CaseId}",
                warningDeadline, input.CaseId);

            using var warnCts = new CancellationTokenSource();
            var warnTimerTask  = context.CreateTimer(warningDeadline, warnCts.Token);
            var warnCancelTask = context.WaitForExternalEvent<bool>("cancel");

            var warnWinner = await Task.WhenAny(warnTimerTask, warnCancelTask);

            if (warnWinner == warnCancelTask)
            {
                log.LogInformation("Case {CaseId} resolved before SLA warning — orchestration cancelled.", input.CaseId);
                warnCts.Cancel();
                return;
            }

            // Timer fired — publish warning
            await context.CallActivityAsync(
                nameof(PublishSlaWarningActivity),
                new CaseActivityInput(input.CaseId, input.TenantId));
        }

        // ── Breach phase ──────────────────────────────────────────────────────
        log.LogInformation(
            "Waiting for SLA breach deadline {Deadline:u} for case {CaseId}",
            input.SlaDeadline, input.CaseId);

        using var breachCts = new CancellationTokenSource();
        var breachTimerTask  = context.CreateTimer(input.SlaDeadline, breachCts.Token);
        var breachCancelTask = context.WaitForExternalEvent<bool>("cancel");

        var breachWinner = await Task.WhenAny(breachTimerTask, breachCancelTask);

        if (breachWinner == breachCancelTask)
        {
            log.LogInformation("Case {CaseId} resolved before SLA breach — orchestration cancelled.", input.CaseId);
            breachCts.Cancel();
            return;
        }

        // Deadline elapsed — mark breached
        log.LogWarning("SLA breached for case {CaseId}!", input.CaseId);

        await context.CallActivityAsync(
            nameof(MarkSlaBreachedActivity),
            new CaseActivityInput(input.CaseId, input.TenantId));
    }

    // ─── Activity: StoreInstanceIdActivity ────────────────────────────────────
    [Function(nameof(StoreInstanceIdActivity))]
    public async Task StoreInstanceIdActivity(
        [ActivityTrigger] CaseActivityInput input)
    {
        var instanceId = $"sla-{input.CaseId}";
        var http       = httpClientFactory.CreateClient("css-service");

        var response = await http.PatchAsync(
            $"internal/cases/{input.CaseId}/instance-id",
            JsonContent(new { InstanceId = instanceId, input.TenantId }));

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "Failed to store Durable Function instance ID on case {CaseId}: {Status}",
                input.CaseId, response.StatusCode);
        }
    }

    // ─── Activity: PublishSlaWarningActivity ──────────────────────────────────
    [Function(nameof(PublishSlaWarningActivity))]
    public async Task PublishSlaWarningActivity(
        [ActivityTrigger] CaseActivityInput input)
    {
        var http = httpClientFactory.CreateClient("css-service");

        var response = await http.PostAsync(
            $"internal/cases/{input.CaseId}/sla-warning",
            JsonContent(new { input.TenantId }));

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "Failed to publish SLA warning for case {CaseId}: {Status}",
                input.CaseId, response.StatusCode);
        }
        else
        {
            logger.LogInformation("SLA warning published for case {CaseId}.", input.CaseId);
        }
    }

    // ─── Activity: MarkSlaBreachedActivity ────────────────────────────────────
    [Function(nameof(MarkSlaBreachedActivity))]
    public async Task MarkSlaBreachedActivity(
        [ActivityTrigger] CaseActivityInput input)
    {
        var http = httpClientFactory.CreateClient("css-service");

        var response = await http.PostAsync(
            $"internal/cases/{input.CaseId}/sla-breach",
            JsonContent(new { input.TenantId }));

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "Failed to mark SLA breach for case {CaseId}: {Status}",
                input.CaseId, response.StatusCode);
        }
        else
        {
            logger.LogWarning("SLA breach recorded for case {CaseId}.", input.CaseId);
        }
    }

    // ─── Helper ───────────────────────────────────────────────────────────────
    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
}
