using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CrmPlatform.AiOrchestrationService.Domain.Entities;
using CrmPlatform.AiOrchestrationService.Domain.Enums;
using CrmPlatform.AiOrchestrationService.Domain.Events;
using CrmPlatform.AiOrchestrationService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.AiOrchestrationService.Infrastructure.Messaging;

// ── crm.sfa consumers ─────────────────────────────────────────────────────────

/// <summary>
/// Consumes crm.sfa / lead.created → queues LeadScoring AiJob.
/// </summary>
public sealed class LeadCreatedConsumer(
    ServiceBusClient                    serviceBusClient,
    IIdempotencyStore                   idempotencyStore,
    IOptions<ServiceBusConsumerOptions> options,
    ILogger<LeadCreatedConsumer>        logger,
    IServiceScopeFactory                scopeFactory)
    : BaseServiceBusConsumer<LeadCreatedEvent>(
        serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.sfa";
    protected override string SubscriptionName => "ai-service-lead-created";

    protected override async Task<bool> ProcessAsync(
        LeadCreatedEvent message,
        Guid             tenantId,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            leadId   = message.LeadId,
            leadData = message.LeadData
        });

        var job = AiJob.Create(
            tenantId:          message.TenantId,
            capabilityType:    CapabilityType.LeadScoring,
            useCase:           UseCase.LeadCreated,
            requestedByUserId: null,
            inputPayload:      payload);

        db.AiJobs.Add(job);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Queued LeadScoring job {JobId} for lead {LeadId}",
            job.Id, message.LeadId);
        return true;
    }
}

/// <summary>
/// Consumes crm.sfa / lead.assigned → queues LeadScoring + NextBestAction AiJobs.
/// </summary>
public sealed class LeadAssignedConsumer(
    ServiceBusClient                    serviceBusClient,
    IIdempotencyStore                   idempotencyStore,
    IOptions<ServiceBusConsumerOptions> options,
    ILogger<LeadAssignedConsumer>       logger,
    IServiceScopeFactory                scopeFactory)
    : BaseServiceBusConsumer<LeadAssignedEvent>(
        serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.sfa";
    protected override string SubscriptionName => "ai-service-lead-assigned";

    protected override async Task<bool> ProcessAsync(
        LeadAssignedEvent message,
        Guid              tenantId,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            leadId         = message.LeadId,
            leadData       = message.LeadData,
            assignedTo     = message.AssignedToUserId,
            entityId       = message.LeadId,
            entityType     = "Lead"
        });

        var scoringJob = AiJob.Create(message.TenantId, CapabilityType.LeadScoring,
            UseCase.LeadAssigned, message.AssignedToUserId, payload);

        var nbaJob = AiJob.Create(message.TenantId, CapabilityType.NextBestAction,
            UseCase.NbaLeadAssigned, message.AssignedToUserId, payload);

        db.AiJobs.AddRange(scoringJob, nbaJob);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Queued LeadScoring {SId} + NextBestAction {NId} for lead {LeadId}",
            scoringJob.Id, nbaJob.Id, message.LeadId);
        return true;
    }
}

/// <summary>
/// Consumes crm.sfa / opportunity.stage_changed → queues NextBestAction AiJob.
/// </summary>
public sealed class OpportunityStageChangedConsumer(
    ServiceBusClient                            serviceBusClient,
    IIdempotencyStore                           idempotencyStore,
    IOptions<ServiceBusConsumerOptions>         options,
    ILogger<OpportunityStageChangedConsumer>    logger,
    IServiceScopeFactory                        scopeFactory)
    : BaseServiceBusConsumer<OpportunityStageChangedEvent>(
        serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.sfa";
    protected override string SubscriptionName => "ai-service-opp-stage";

    protected override async Task<bool> ProcessAsync(
        OpportunityStageChangedEvent message,
        Guid                         tenantId,
        CancellationToken            ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            entityId         = message.OpportunityId,
            entityType       = "Opportunity",
            opportunityData  = message.OpportunityData,
            oldStage         = message.OldStage,
            newStage         = message.NewStage
        });

        var job = AiJob.Create(message.TenantId, CapabilityType.NextBestAction,
            UseCase.NbaOpportunityStageChanged, null, payload);

        db.AiJobs.Add(job);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Queued NextBestAction {JobId} for opportunity {OppId}",
            job.Id, message.OpportunityId);
        return true;
    }
}

// ── crm.css consumers ─────────────────────────────────────────────────────────

/// <summary>
/// Consumes crm.css / case.resolved → queues CaseSummarisation AiJob.
/// </summary>
public sealed class CaseResolvedConsumer(
    ServiceBusClient                    serviceBusClient,
    IIdempotencyStore                   idempotencyStore,
    IOptions<ServiceBusConsumerOptions> options,
    ILogger<CaseResolvedConsumer>       logger,
    IServiceScopeFactory                scopeFactory)
    : BaseServiceBusConsumer<CaseResolvedEvent>(
        serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.css";
    protected override string SubscriptionName => "ai-service-case-resolved";

    protected override async Task<bool> ProcessAsync(
        CaseResolvedEvent message,
        Guid              tenantId,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            caseId   = message.CaseId,
            caseData = message.CaseSummaryData
        });

        var job = AiJob.Create(message.TenantId, CapabilityType.CaseSummarisation,
            UseCase.CaseResolved, null, payload);

        db.AiJobs.Add(job);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Queued CaseSummarisation {JobId} for case {CaseId}",
            job.Id, message.CaseId);
        return true;
    }
}

/// <summary>
/// Consumes crm.css / case.comment_added → queues SentimentAnalysis AiJob.
/// </summary>
public sealed class CaseCommentAddedConsumer(
    ServiceBusClient                      serviceBusClient,
    IIdempotencyStore                     idempotencyStore,
    IOptions<ServiceBusConsumerOptions>   options,
    ILogger<CaseCommentAddedConsumer>     logger,
    IServiceScopeFactory                  scopeFactory)
    : BaseServiceBusConsumer<CaseCommentAddedEvent>(
        serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.css";
    protected override string SubscriptionName => "ai-service-comment-added";

    protected override async Task<bool> ProcessAsync(
        CaseCommentAddedEvent message,
        Guid                  tenantId,
        CancellationToken     ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            caseId      = message.CaseId,
            commentId   = message.CommentId,
            commentText = message.CommentText
        });

        var job = AiJob.Create(message.TenantId, CapabilityType.SentimentAnalysis,
            UseCase.CaseCommentAdded, null, payload);

        db.AiJobs.Add(job);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Queued SentimentAnalysis {JobId} for comment {CommentId}",
            job.Id, message.CommentId);
        return true;
    }
}

// ── crm.marketing consumers ───────────────────────────────────────────────────

/// <summary>
/// Consumes crm.marketing / journey.enrollment_created → queues JourneyPersonalisation AiJob.
/// </summary>
public sealed class JourneyEnrollmentCreatedConsumer(
    ServiceBusClient                            serviceBusClient,
    IIdempotencyStore                           idempotencyStore,
    IOptions<ServiceBusConsumerOptions>         options,
    ILogger<JourneyEnrollmentCreatedConsumer>   logger,
    IServiceScopeFactory                        scopeFactory)
    : BaseServiceBusConsumer<JourneyEnrollmentCreatedEvent>(
        serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.marketing";
    protected override string SubscriptionName => "ai-service-enrollment";

    protected override async Task<bool> ProcessAsync(
        JourneyEnrollmentCreatedEvent message,
        Guid                          tenantId,
        CancellationToken             ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            enrollmentId   = message.EnrollmentId,
            contactId      = message.ContactId,
            journeyId      = message.JourneyId,
            enrollmentData = message.EnrollmentData
        });

        var job = AiJob.Create(message.TenantId, CapabilityType.JourneyPersonalisation,
            UseCase.JourneyEnrollmentCreated, null, payload);

        db.AiJobs.Add(job);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Queued JourneyPersonalisation {JobId} for enrollment {EnrollmentId}",
            job.Id, message.EnrollmentId);
        return true;
    }
}

// ── crm.platform consumers ────────────────────────────────────────────────────

/// <summary>
/// Consumes crm.platform / tenant.provisioned — currently a no-op placeholder.
/// </summary>
public sealed class TenantProvisionedConsumer(
    ServiceBusClient                    serviceBusClient,
    IIdempotencyStore                   idempotencyStore,
    IOptions<ServiceBusConsumerOptions> options,
    ILogger<TenantProvisionedConsumer>  logger,
    IServiceScopeFactory                scopeFactory)
    : BaseServiceBusConsumer<TenantProvisionedEvent>(
        serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.platform";
    protected override string SubscriptionName => "ai-service-tenant-provisioned";

    protected override Task<bool> ProcessAsync(
        TenantProvisionedEvent message,
        Guid                   tenantId,
        CancellationToken      ct)
    {
        logger.LogInformation("Tenant provisioned: {TenantId} ({TenantName})",
            message.TenantId, message.TenantName);
        return Task.FromResult(true);
    }
}

/// <summary>
/// Consumes crm.platform / tenant.suspended — abandons all queued AiJobs for the tenant.
/// </summary>
public sealed class TenantSuspendedConsumer(
    ServiceBusClient                    serviceBusClient,
    IIdempotencyStore                   idempotencyStore,
    IOptions<ServiceBusConsumerOptions> options,
    ILogger<TenantSuspendedConsumer>    logger,
    IServiceScopeFactory                scopeFactory)
    : BaseServiceBusConsumer<TenantSuspendedEvent>(
        serviceBusClient, idempotencyStore, options, logger)
{
    protected override string TopicName        => "crm.platform";
    protected override string SubscriptionName => "ai-service-tenant-suspended";

    protected override async Task<bool> ProcessAsync(
        TenantSuspendedEvent message,
        Guid                 tenantId,
        CancellationToken    ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();

        var queuedJobs = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToListAsync(
                db.AiJobs.IgnoreQueryFilters()
                    .Where(j => j.TenantId == message.TenantId &&
                                (j.Status == AiJobStatus.Queued || j.Status == AiJobStatus.Failed)),
                ct);

        foreach (var job in queuedJobs)
            job.Abandon("Tenant suspended.");

        if (queuedJobs.Count > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation("Abandoned {Count} AI jobs for suspended tenant {TenantId}",
            queuedJobs.Count, message.TenantId);
        return true;
    }
}
