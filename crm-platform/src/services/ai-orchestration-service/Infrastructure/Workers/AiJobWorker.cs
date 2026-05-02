using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using CrmPlatform.AiOrchestrationService.Domain.Entities;
using CrmPlatform.AiOrchestrationService.Domain.Enums;
using CrmPlatform.AiOrchestrationService.Domain.Events;
using CrmPlatform.AiOrchestrationService.Infrastructure.Claude;
using CrmPlatform.AiOrchestrationService.Infrastructure.Data;
using CrmPlatform.AiOrchestrationService.Infrastructure.Sms;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.AiOrchestrationService.Infrastructure.Workers;

/// <summary>
/// Background worker that polls AiJobs and executes AI capabilities.
/// Runs every 5 seconds. Max 3 attempts, 30s fixed retry delay.
/// Jobs unprocessed for >1 hour are auto-abandoned (stale TTL).
/// </summary>
public sealed class AiJobWorker(
    IServiceScopeFactory     scopeFactory,
    ILogger<AiJobWorker>     logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval  = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay    = TimeSpan.FromSeconds(30);
    private const int MaxAttempts = 3;
    private const int BatchSize   = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AiJobWorker started");

        using var timer = new PeriodicTimer(PollInterval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
                await AbandonStaleJobsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unhandled error in AiJobWorker");
            }
        }
    }

    // ── Stale TTL sweep ───────────────────────────────────────────────────────

    private async Task AbandonStaleJobsAsync(CancellationToken ct)
    {
        await using var scope     = scopeFactory.CreateAsyncScope();
        var db                    = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        var publisher             = scope.ServiceProvider.GetRequiredService<ServiceBusEventPublisher>();

        var cutoff = DateTime.UtcNow.AddHours(-1);
        var stale  = await db.AiJobs
            .Where(j => j.Status == AiJobStatus.Queued && j.CreatedAt < cutoff)
            .ToListAsync(ct);

        if (stale.Count == 0) return;

        logger.LogWarning("AiJobWorker abandoning {Count} stale jobs", stale.Count);

        foreach (var job in stale)
        {
            job.Abandon("Job exceeded 1-hour TTL without being processed.");
            await publisher.PublishAsync(
                "crm.ai",
                new AiJobStalledEvent(
                    job.TenantId,
                    job.Id,
                    job.CapabilityType,
                    job.UseCase,
                    job.RequestedByUserId,
                    job.CreatedAt),
                ct);
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Job processing batch ──────────────────────────────────────────────────

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope     = scopeFactory.CreateAsyncScope();
        var db                    = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        var claude                = scope.ServiceProvider.GetRequiredService<IClaudeClient>();
        var smsClient             = scope.ServiceProvider.GetRequiredService<IAiSmsClient>();
        var publisher             = scope.ServiceProvider.GetRequiredService<ServiceBusEventPublisher>();

        var now  = DateTime.UtcNow;
        var jobs = await db.AiJobs
            .Where(j =>
                (j.Status == AiJobStatus.Queued ||
                 (j.Status == AiJobStatus.Failed && j.NextRetryAt != null && j.NextRetryAt <= now)) &&
                j.AttemptCount < MaxAttempts)
            .OrderBy(j => j.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (jobs.Count == 0) return;

        logger.LogInformation("AiJobWorker processing {Count} jobs", jobs.Count);

        foreach (var job in jobs)
        {
            try
            {
                await ExecuteJobAsync(job, db, claude, smsClient, publisher, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error executing job {JobId}", job.Id);
            }
        }
    }

    private async Task ExecuteJobAsync(
        AiJob                   job,
        AiDbContext             db,
        IClaudeClient           claude,
        IAiSmsClient            smsClient,
        ServiceBusEventPublisher publisher,
        CancellationToken       ct)
    {
        job.MarkInProgress();
        await db.SaveChangesAsync(ct);

        try
        {
            var input = JsonSerializer.Deserialize<Dictionary<string, object>>(job.InputPayload)
                     ?? new Dictionary<string, object>();

            ClaudeResponse aiResponse;
            AiResult       result;

            switch (job.CapabilityType)
            {
                // ── Lead scoring ─────────────────────────────────────────────
                case CapabilityType.LeadScoring:
                    aiResponse = await claude.CompleteAsync(job.TenantId, job.CapabilityType, job.UseCase, input, ct);
                    result     = RecordResult(db, job, aiResponse);
                    await db.SaveChangesAsync(ct);
                    job.MarkSucceeded(result.Id);
                    await db.SaveChangesAsync(ct);
                    await PublishLeadScoredAsync(job, aiResponse, publisher, ct);
                    break;

                // ── Case summarisation ────────────────────────────────────────
                case CapabilityType.CaseSummarisation:
                    aiResponse = await claude.CompleteAsync(job.TenantId, job.CapabilityType, job.UseCase, input, ct);
                    result     = RecordResult(db, job, aiResponse);
                    await db.SaveChangesAsync(ct);
                    job.MarkSucceeded(result.Id);
                    await db.SaveChangesAsync(ct);
                    await PublishCaseSummarisedAsync(job, aiResponse, publisher, ct);
                    break;

                // ── Sentiment analysis ────────────────────────────────────────
                case CapabilityType.SentimentAnalysis:
                    aiResponse = await claude.CompleteAsync(job.TenantId, job.CapabilityType, job.UseCase, input, ct);
                    result     = RecordResult(db, job, aiResponse);
                    await db.SaveChangesAsync(ct);
                    job.MarkSucceeded(result.Id);
                    await db.SaveChangesAsync(ct);
                    await PublishSentimentAsync(job, aiResponse, publisher, ct);
                    break;

                // ── Next best action ──────────────────────────────────────────
                case CapabilityType.NextBestAction:
                    aiResponse = await claude.CompleteAsync(job.TenantId, job.CapabilityType, job.UseCase, input, ct);
                    result     = RecordResult(db, job, aiResponse);
                    await db.SaveChangesAsync(ct);
                    job.MarkSucceeded(result.Id);
                    await db.SaveChangesAsync(ct);
                    await PublishNextBestActionAsync(job, aiResponse, publisher, ct);
                    break;

                // ── Journey personalisation ───────────────────────────────────
                case CapabilityType.JourneyPersonalisation:
                    aiResponse = await claude.CompleteAsync(job.TenantId, job.CapabilityType, job.UseCase, input, ct);
                    result     = RecordResult(db, job, aiResponse);
                    await db.SaveChangesAsync(ct);
                    job.MarkSucceeded(result.Id);
                    await db.SaveChangesAsync(ct);
                    await PublishJourneyPersonalisedAsync(job, aiResponse, publisher, ct);
                    break;

                // ── SMS composition ───────────────────────────────────────────
                case CapabilityType.SmsComposition:
                    aiResponse = await claude.CompleteAsync(job.TenantId, job.CapabilityType, job.UseCase, input, ct);
                    result     = RecordResult(db, job, aiResponse);
                    var toPhone   = input.GetValueOrDefault("recipientPhone")?.ToString() ?? string.Empty;
                    var fromPhone = input.GetValueOrDefault("fromPhone")?.ToString()      ?? string.Empty;
                    var smsRecord = SmsRecord.Create(job.TenantId, toPhone, aiResponse.Content, job.Id);
                    db.SmsRecords.Add(smsRecord);
                    await db.SaveChangesAsync(ct);
                    try
                    {
                        var acsId = await smsClient.SendAsync(fromPhone, toPhone, aiResponse.Content, ct);
                        smsRecord.MarkSent(acsId);
                    }
                    catch (Exception ex)
                    {
                        smsRecord.MarkFailed(ex.Message);
                    }
                    await db.SaveChangesAsync(ct);
                    job.MarkSucceeded(result.Id);
                    await db.SaveChangesAsync(ct);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Capability {job.CapabilityType} is not handled by AiJobWorker (sync capabilities not queued).");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {JobId} attempt {Attempt} failed: {Message}",
                job.Id, job.AttemptCount, ex.Message);

            bool exhausted = job.AttemptCount >= MaxAttempts;
            if (exhausted)
            {
                job.MarkFailed(ex.Message);
                await db.SaveChangesAsync(ct);
                await publisher.PublishAsync(
                    "crm.ai",
                    new AiJobFailedEvent(
                        job.TenantId,
                        job.Id,
                        job.CapabilityType,
                        job.UseCase,
                        job.RequestedByUserId,
                        ex.Message),
                    ct);
            }
            else
            {
                job.MarkFailed(ex.Message, nextRetryAt: DateTime.UtcNow.Add(RetryDelay));
                await db.SaveChangesAsync(ct);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AiResult RecordResult(AiDbContext db, AiJob job, ClaudeResponse r)
    {
        var result = AiResult.Record(
            job.TenantId, job.Id, job.CapabilityType, job.UseCase,
            r.ModelName, r.PromptUsed, r.Content, r.InputTokens, r.OutputTokens);
        db.AiResults.Add(result);
        return result;
    }

    private static T ParseJson<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException($"Failed to parse AI response: {json}");
    }

    // ── Event publishers ──────────────────────────────────────────────────────

    private async Task PublishLeadScoredAsync(
        AiJob job, ClaudeResponse r, ServiceBusEventPublisher p, CancellationToken ct)
    {
        var parsed = ParseJson<LeadScoreOutput>(r.Content);
        var input  = JsonSerializer.Deserialize<Dictionary<string, object>>(job.InputPayload)!;
        var leadId = Guid.Parse(input["leadId"].ToString()!);

        await p.PublishAsync("crm.ai", new LeadScoredEvent(
            job.TenantId, leadId, job.Id,
            parsed.Score, parsed.Rationale, parsed.Confidence), ct);
    }

    private async Task PublishCaseSummarisedAsync(
        AiJob job, ClaudeResponse r, ServiceBusEventPublisher p, CancellationToken ct)
    {
        var input  = JsonSerializer.Deserialize<Dictionary<string, object>>(job.InputPayload)!;
        var caseId = Guid.Parse(input["caseId"].ToString()!);

        await p.PublishAsync("crm.ai", new CaseSummarisedEvent(
            job.TenantId, caseId, job.Id, r.Content), ct);
    }

    private async Task PublishSentimentAsync(
        AiJob job, ClaudeResponse r, ServiceBusEventPublisher p, CancellationToken ct)
    {
        var parsed    = ParseJson<SentimentOutput>(r.Content);
        var input     = JsonSerializer.Deserialize<Dictionary<string, object>>(job.InputPayload)!;
        var caseId    = Guid.Parse(input["caseId"].ToString()!);
        var commentId = Guid.Parse(input["commentId"].ToString()!);

        var label = Enum.Parse<SentimentLabel>(parsed.Sentiment, ignoreCase: true);

        await p.PublishAsync("crm.ai", new SentimentAnalysedEvent(
            job.TenantId, caseId, commentId, job.Id, label, parsed.Score), ct);
    }

    private async Task PublishNextBestActionAsync(
        AiJob job, ClaudeResponse r, ServiceBusEventPublisher p, CancellationToken ct)
    {
        var parsed   = ParseJson<NbaOutput>(r.Content);
        var input    = JsonSerializer.Deserialize<Dictionary<string, object>>(job.InputPayload)!;
        var entityId = Guid.Parse(input["entityId"].ToString()!);
        var entityType = input.GetValueOrDefault("entityType")?.ToString() ?? "Lead";

        await p.PublishAsync("crm.ai", new NextBestActionGeneratedEvent(
            job.TenantId, entityId, entityType, job.Id,
            parsed.Action, parsed.Rationale), ct);
    }

    private async Task PublishJourneyPersonalisedAsync(
        AiJob job, ClaudeResponse r, ServiceBusEventPublisher p, CancellationToken ct)
    {
        var parsed       = ParseJson<JourneyPersonalisationOutput>(r.Content);
        var input        = JsonSerializer.Deserialize<Dictionary<string, object>>(job.InputPayload)!;
        var enrollmentId = Guid.Parse(input["enrollmentId"].ToString()!);

        await p.PublishAsync("crm.ai", new JourneyPersonalisedEvent(
            job.TenantId, enrollmentId, job.Id,
            parsed.RecommendedBranchId, parsed.Rationale), ct);
    }

    // ── Output DTOs (parsed from Claude JSON responses) ───────────────────────

    private sealed record LeadScoreOutput(int Score, string Rationale, double Confidence);
    private sealed record SentimentOutput(string Sentiment, double Score);
    private sealed record NbaOutput(string Action, string Rationale);
    private sealed record JourneyPersonalisationOutput(Guid RecommendedBranchId, string Rationale);
}
