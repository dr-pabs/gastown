using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using CrmPlatform.AiOrchestrationService.Api.Dtos;
using CrmPlatform.AiOrchestrationService.Domain.Entities;
using CrmPlatform.AiOrchestrationService.Domain.Enums;
using CrmPlatform.AiOrchestrationService.Infrastructure.Claude;
using CrmPlatform.AiOrchestrationService.Infrastructure.Data;
using CrmPlatform.AiOrchestrationService.Infrastructure.Teams;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.AiOrchestrationService.Application;

// ── Sync AI handlers ──────────────────────────────────────────────────────────

/// <summary>
/// Handles synchronous (direct-response) AI requests: email draft, Teams card, Teams call.
/// </summary>
public sealed class SyncAiHandler(
    AiDbContext                db,
    IClaudeClient              claude,
    ITeamsNotificationClient   teamsNotify,
    ITeamsCallingClient        teamsCalling,
    IPromptTemplateReader      promptReader,
    ITenantContextAccessor     tenantAccessor,
    IConfiguration             config,
    ILogger<SyncAiHandler>     logger)
{
    // ── Email Draft ───────────────────────────────────────────────────────────

    public async Task<EmailDraftResponse> DraftEmailAsync(
        EmailDraftRequest req,
        CancellationToken ct)
    {
        var tenantId = tenantAccessor.TenantId;

        var vars     = new { leadName = req.LeadName, company = req.Company, productInterest = req.ProductInterest };
        var response = await claude.CompleteAsync(
            tenantId, CapabilityType.EmailDraft, req.UseCase, vars, ct);

        var result = AiResult.Record(
            tenantId, jobId: null,
            CapabilityType.EmailDraft, req.UseCase,
            response.ModelName, response.PromptUsed, response.Content,
            response.InputTokens, response.OutputTokens);

        db.AiResults.Add(result);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Email draft generated resultId={ResultId}", result.Id);
        return new EmailDraftResponse(result.Id, response.Content);
    }

    // ── Teams Adaptive Card ───────────────────────────────────────────────────

    public async Task<TeamsNotificationResponse> SendTeamsCardAsync(
        TeamsNotificationRequest req,
        CancellationToken        ct)
    {
        var tenantId   = tenantAccessor.TenantId;
        var webhookUrl = await ResolveWebhookUrlAsync(tenantId, ct);

        // Compose card content via Claude
        var vars     = new { context = req.Context };
        var response = await claude.CompleteAsync(
            tenantId, CapabilityType.TeamsNotification, UseCase.TeamsAdaptiveCard, vars, ct);

        await teamsNotify.SendAdaptiveCardAsync(webhookUrl, req.CardTitle, response.Content, ct);

        var result = AiResult.Record(
            tenantId, jobId: null,
            CapabilityType.TeamsNotification, UseCase.TeamsAdaptiveCard,
            response.ModelName, response.PromptUsed, response.Content,
            response.InputTokens, response.OutputTokens);

        db.AiResults.Add(result);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Teams card sent resultId={ResultId}", result.Id);
        return new TeamsNotificationResponse(result.Id, Sent: true);
    }

    // ── Teams Outbound Call ───────────────────────────────────────────────────

    public async Task<TeamsCallResponse> InitiateCallAsync(
        TeamsCallRequest  req,
        CancellationToken ct)
    {
        var tenantId      = tenantAccessor.TenantId;
        var callbackUri   = config["Azure:CommunicationServices:CallbackUri"]!;
        var callContext   = JsonSerializer.Serialize(req);

        var callRecord = TeamsCallRecord.Create(tenantId, req.TargetAcsUserId, callContext);
        db.TeamsCallRecords.Add(callRecord);
        await db.SaveChangesAsync(ct);

        var initiated = await teamsCalling.InitiateCallAsync(req.TargetAcsUserId, callbackUri, ct);
        callRecord.MarkConnected(initiated.CallId);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Teams call initiated callRecordId={RecordId} callId={CallId}",
            callRecord.Id, initiated.CallId);
        return new TeamsCallResponse(callRecord.Id, initiated.CallId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> ResolveWebhookUrlAsync(Guid tenantId, CancellationToken ct)
    {
        // Webhook URL stored in Key Vault: teams-webhook-{tenantId}
        // For simplicity, fall back to config in non-prod
        var fromConfig = config[$"Teams:WebhookUrls:{tenantId}"];
        if (!string.IsNullOrEmpty(fromConfig)) return fromConfig;

        var fallback = config["Teams:DefaultWebhookUrl"];
        if (!string.IsNullOrEmpty(fallback)) return fallback;

        throw new InvalidOperationException($"No Teams webhook URL configured for tenant {tenantId}");
    }
}

// ── Async job dispatch handler ────────────────────────────────────────────────

/// <summary>
/// Enqueues AI jobs for async processing (SMS composition, on-demand case summarisation).
/// </summary>
public sealed class AsyncAiJobHandler(
    AiDbContext                db,
    ITenantContextAccessor     tenantAccessor,
    ILogger<AsyncAiJobHandler> logger)
{
    public async Task<AiJobResponse> EnqueueSmsAsync(
        SmsComposeRequest req,
        Guid?             requestedByUserId,
        CancellationToken ct)
    {
        var tenantId = tenantAccessor.TenantId;
        var payload  = JsonSerializer.Serialize(new
        {
            recipientPhone = req.RecipientPhone,
            fromPhone      = req.FromPhone,
            campaignName   = req.CampaignName,
            goal           = req.Goal
        });

        var job = AiJob.Create(tenantId, CapabilityType.SmsComposition,
            req.UseCase, requestedByUserId, payload);
        db.AiJobs.Add(job);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Queued SMS composition job {JobId}", job.Id);
        return AiJobResponse.From(job);
    }

    public async Task<AiJobResponse> EnqueueCaseSummarisationAsync(
        CaseSummarisationRequest req,
        Guid?                    requestedByUserId,
        CancellationToken        ct)
    {
        var tenantId = tenantAccessor.TenantId;
        var payload  = JsonSerializer.Serialize(new
        {
            caseId   = req.CaseId,
            caseData = req.CaseData
        });

        var job = AiJob.Create(tenantId, CapabilityType.CaseSummarisation,
            UseCase.CaseOnDemand, requestedByUserId, payload);
        db.AiJobs.Add(job);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Queued on-demand CaseSummarisation job {JobId}", job.Id);
        return AiJobResponse.From(job);
    }
}

// ── Prompt management handler ─────────────────────────────────────────────────

/// <summary>
/// Manages tenant custom prompt templates. Editable by TenantAdmin or AiPromptEditor.
/// </summary>
public sealed class PromptManagementHandler(
    AiDbContext                        db,
    ITenantContextAccessor             tenantAccessor,
    ILogger<PromptManagementHandler>   logger)
{
    public async Task<List<PromptTemplateResponse>> ListAsync(CancellationToken ct)
    {
        var tenantId = tenantAccessor.TenantId;
        return await db.PromptTemplates
            .Where(p => p.TenantId == tenantId && !p.IsDeleted)
            .OrderBy(p => p.CapabilityType)
            .ThenBy(p => p.UseCase)
            .Select(p => PromptTemplateResponse.From(p))
            .ToListAsync(ct);
    }

    public async Task<PromptTemplateResponse> UpsertAsync(
        UpsertPromptTemplateRequest req,
        CancellationToken           ct)
    {
        var tenantId = tenantAccessor.TenantId;

        var existing = await db.PromptTemplates
            .FirstOrDefaultAsync(p =>
                p.TenantId       == tenantId     &&
                p.CapabilityType == req.CapabilityType &&
                p.UseCase        == req.UseCase   &&
                !p.IsDeleted, ct);

        if (existing is not null)
        {
            existing.Update(req.SystemPrompt, req.UserPromptTemplate);
            logger.LogInformation("Updated prompt template {Id}", existing.Id);
        }
        else
        {
            existing = PromptTemplate.Create(tenantId, req.CapabilityType, req.UseCase,
                req.SystemPrompt, req.UserPromptTemplate);
            db.PromptTemplates.Add(existing);
            logger.LogInformation("Created prompt template {Id}", existing.Id);
        }

        await db.SaveChangesAsync(ct);
        return PromptTemplateResponse.From(existing);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var tenantId = tenantAccessor.TenantId;
        var template = await db.PromptTemplates
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException($"Prompt template {id} not found.");

        template.Delete();
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Deleted prompt template {Id}", id);
    }
}

// ── Read-model handlers ───────────────────────────────────────────────────────

/// <summary>Query handlers for AiJobs, AiResults, SmsRecords, TeamsCallRecords.</summary>
public sealed class AiReadHandler(
    AiDbContext            db,
    ITenantContextAccessor tenantAccessor)
{
    public async Task<PagedResult<AiJobResponse>> ListJobsAsync(
        AiJobStatus?   status,
        CapabilityType? capability,
        int page, int pageSize,
        CancellationToken ct)
    {
        var tenantId = tenantAccessor.TenantId;
        var q = db.AiJobs.Where(j => j.TenantId == tenantId);

        if (status.HasValue)     q = q.Where(j => j.Status       == status.Value);
        if (capability.HasValue) q = q.Where(j => j.CapabilityType == capability.Value);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(j => AiJobResponse.From(j))
            .ToListAsync(ct);

        return new PagedResult<AiJobResponse>(items, total, page, pageSize);
    }

    public async Task<AiJobResponse?> GetJobAsync(Guid id, CancellationToken ct)
    {
        var tenantId = tenantAccessor.TenantId;
        var job = await db.AiJobs
            .FirstOrDefaultAsync(j => j.Id == id && j.TenantId == tenantId, ct);
        return job is null ? null : AiJobResponse.From(job);
    }

    public async Task<AiResultResponse?> GetResultAsync(Guid id, CancellationToken ct)
    {
        var tenantId = tenantAccessor.TenantId;
        var result = await db.AiResults
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        return result is null ? null : AiResultResponse.From(result);
    }

    public async Task<PagedResult<SmsRecordResponse>> ListSmsRecordsAsync(
        int page, int pageSize, CancellationToken ct)
    {
        var tenantId = tenantAccessor.TenantId;
        var q = db.SmsRecords.Where(s => s.TenantId == tenantId);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => SmsRecordResponse.From(s))
            .ToListAsync(ct);

        return new PagedResult<SmsRecordResponse>(items, total, page, pageSize);
    }

    public async Task<PagedResult<TeamsCallResponse>> ListTeamsCallsAsync(
        int page, int pageSize, CancellationToken ct)
    {
        var tenantId = tenantAccessor.TenantId;
        var q = db.TeamsCallRecords.Where(c => c.TenantId == tenantId);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new TeamsCallResponse(c.Id, c.AcsCallId))
            .ToListAsync(ct);

        return new PagedResult<TeamsCallResponse>(items, total, page, pageSize);
    }
}

/// <summary>Generic paged result wrapper.</summary>
public sealed record PagedResult<T>(
    List<T> Items,
    int     Total,
    int     Page,
    int     PageSize);
