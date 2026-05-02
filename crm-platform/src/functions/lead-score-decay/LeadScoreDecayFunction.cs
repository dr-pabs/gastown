using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CrmPlatform.Functions.LeadScoreDecay;

// ──────────────────────────────────────────────────────────────────────────────
// Payload
// ──────────────────────────────────────────────────────────────────────────────
/// <summary>
/// Passed as ContinueAsNew input so the orchestrator knows when its next
/// scheduled execution should fire (next midnight UTC).
/// </summary>
public sealed record DecaySchedule(DateTime NextRunUtc);

// ──────────────────────────────────────────────────────────────────────────────
// Function class
// ──────────────────────────────────────────────────────────────────────────────
public class LeadScoreDecayFunction(IHttpClientFactory httpClientFactory, ILogger<LeadScoreDecayFunction> logger)
{
    private const string InstanceId = "lead-score-decay-singleton";

    // ─── HTTP starter (one-time bootstrap, idempotent) ────────────────────────
    /// <summary>
    /// POST /api/start-lead-decay  — call once at deployment to seed the
    /// eternal orchestration.  Subsequent calls are ignored if already running.
    /// </summary>
    [Function(nameof(StartLeadDecay))]
    public async Task<HttpResponseData> StartLeadDecay(
        [HttpTrigger(AuthorizationLevel.Admin, "post", Route = "start-lead-decay")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        var existing = await client.GetInstanceAsync(InstanceId);
        if (existing is not null &&
            (existing.RuntimeStatus == OrchestrationRuntimeStatus.Running ||
             existing.RuntimeStatus == OrchestrationRuntimeStatus.Pending))
        {
            logger.LogInformation("Lead score decay singleton already running — skipping start.");
            var conflict = req.CreateResponse(System.Net.HttpStatusCode.Conflict);
            await conflict.WriteStringAsync("Singleton already running.");
            return conflict;
        }

        // Schedule first run at next midnight UTC
        var tomorrow = DateTime.UtcNow.Date.AddDays(1);
        await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(LeadDecayOrchestrator),
            new DecaySchedule(tomorrow),
            new StartOrchestrationOptions { InstanceId = InstanceId });

        logger.LogInformation("Lead score decay singleton started. First run at {NextRun:u}.", tomorrow);

        var ok = req.CreateResponse(System.Net.HttpStatusCode.Accepted);
        await ok.WriteStringAsync($"Lead score decay started. Next run: {tomorrow:u}");
        return ok;
    }

    // ─── Eternal Orchestrator ─────────────────────────────────────────────────
    [Function(nameof(LeadDecayOrchestrator))]
    public static async Task LeadDecayOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var log      = context.CreateReplaySafeLogger(nameof(LeadDecayOrchestrator));
        var schedule = context.GetInput<DecaySchedule>()!;

        // Wait until next scheduled midnight
        if (context.CurrentUtcDateTime < schedule.NextRunUtc)
        {
            log.LogInformation(
                "Lead score decay sleeping until {NextRun:u}",
                schedule.NextRunUtc);

            await context.CreateTimer(schedule.NextRunUtc, CancellationToken.None);
        }

        log.LogInformation("Triggering lead score decay at {Now:u}", context.CurrentUtcDateTime);

        // Execute decay across all tenants
        await context.CallActivityAsync(nameof(DecayLeadScoresActivity), (object?)null);

        // Schedule next midnight UTC and loop via ContinueAsNew
        var nextMidnight = context.CurrentUtcDateTime.Date.AddDays(1);
        log.LogInformation("Lead score decay complete. Next run: {Next:u}", nextMidnight);

        context.ContinueAsNew(new DecaySchedule(nextMidnight));
    }

    // ─── Activity: DecayLeadScoresActivity ────────────────────────────────────
    /// <summary>
    /// Calls the sfa-service internal decay endpoint which applies the
    /// configurable decay percentage to all leads whose score has not been
    /// updated within the decay window (default: 30 days of inactivity → –10%).
    /// </summary>
    [Function(nameof(DecayLeadScoresActivity))]
    public async Task DecayLeadScoresActivity(
        [ActivityTrigger] object? _)
    {
        var http = httpClientFactory.CreateClient("sfa-service");

        var response = await http.PostAsync(
            "internal/leads/decay-scores",
            new StringContent(
                JsonSerializer.Serialize(new { TriggeredAt = DateTime.UtcNow }),
                System.Text.Encoding.UTF8,
                "application/json"));

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "Lead score decay activity failed: {Status} — will retry on next schedule.",
                response.StatusCode);

            // Intentionally not throwing — a failed decay run should not
            // crash the singleton. Errors are surfaced via Application Insights.
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        logger.LogInformation("Lead score decay complete. Response: {Body}", body);
    }
}
