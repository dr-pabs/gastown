using CrmPlatform.IntegrationService.Domain.Entities;
using CrmPlatform.IntegrationService.Domain.Enums;
using CrmPlatform.IntegrationService.Domain.Events;
using CrmPlatform.IntegrationService.Infrastructure.Connectors;
using CrmPlatform.IntegrationService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.IntegrationService.Infrastructure.Workers;

/// <summary>
/// Background worker that polls OutboundJobs and dispatches them via connector adapters.
/// Runs every 10 seconds. Picks up Queued jobs and Failed jobs whose NextRetryAt has passed.
/// Uses time-bounded retry: abandons jobs when RetryPolicy.MaxRetryDurationMinutes exceeded.
/// </summary>
public sealed class OutboundDispatchWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboundDispatchWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboundDispatchWorker started");

        using var timer = new PeriodicTimer(PollInterval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never crash the worker loop
                logger.LogError(ex, "Unhandled error in OutboundDispatchWorker");
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope     = scopeFactory.CreateAsyncScope();
        var db                    = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        var adapters              = scope.ServiceProvider.GetRequiredService<IEnumerable<IConnectorAdapter>>();
        var tokenStore            = scope.ServiceProvider.GetRequiredService<IConnectorTokenStore>();
        var publisher             = scope.ServiceProvider.GetRequiredService<ServiceBusEventPublisher>();

        var adapterMap = adapters.ToDictionary(a => a.ConnectorType);

        var now  = DateTime.UtcNow;
        var jobs = await db.OutboundJobs
            .Include(j => j.ConnectorConfig)
            .Where(j =>
                (j.Status == OutboundJobStatus.Queued ||
                 (j.Status == OutboundJobStatus.Failed && j.NextRetryAt <= now)) &&
                j.ConnectorConfig.Status == ConnectorStatus.Connected)
            .OrderBy(j => j.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (jobs.Count == 0) return;

        logger.LogInformation("OutboundDispatchWorker processing {Count} jobs", jobs.Count);

        foreach (var job in jobs)
        {
            try
            {
                await DispatchJobAsync(job, adapterMap, tokenStore, publisher, db, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error dispatching job {JobId}", job.Id);
            }
        }

        // Single SaveChanges for the whole batch
        await db.SaveChangesAsync(ct);

        // Publish any queued domain events
        await publisher.PublishPendingAsync(ct);
    }

    private async Task DispatchJobAsync(
        OutboundJob job,
        Dictionary<ConnectorType, IConnectorAdapter> adapterMap,
        IConnectorTokenStore tokenStore,
        ServiceBusEventPublisher publisher,
        IntegrationDbContext db,
        CancellationToken ct)
    {
        var config      = job.ConnectorConfig;
        var retryPolicy = config.RetryPolicy;

        // Check if retry window has been exceeded
        if (retryPolicy.IsWindowExceeded(job.FirstAttemptAt))
        {
            job.Abandon($"Retry window of {retryPolicy.MaxRetryDurationMinutes} minutes exceeded after {job.AttemptCount} attempts");
            publisher.Enqueue("crm.integrations", new OutboundJobFailedEvent(
                TenantId:      job.TenantId,
                OutboundJobId: job.Id,
                ConnectorType: config.ConnectorType.ToString(),
                EventType:     job.EventType,
                FailureReason: job.FailureReason!,
                AttemptCount:  job.AttemptCount));

            logger.LogWarning("Job {JobId} abandoned after {Attempts} attempts", job.Id, job.AttemptCount);
            return;
        }

        if (!adapterMap.TryGetValue(config.ConnectorType, out var adapter))
        {
            job.Abandon($"No adapter registered for connector type {config.ConnectorType}");
            return;
        }

        job.MarkInProgress();

        // Fetch fresh access token from Key Vault
        string? accessToken = null;
        if (!string.IsNullOrEmpty(config.KeyVaultSecretName))
        {
            accessToken = await tokenStore.GetRefreshTokenAsync(config.KeyVaultSecretName, ct);
        }

        var result = await adapter.SendAsync(job, config, accessToken, ct);

        if (result.Success)
        {
            job.MarkSucceeded(result.ExternalId);
            logger.LogInformation(
                "Job {JobId} succeeded for connector {ConnectorType} (ExternalId={ExternalId})",
                job.Id, config.ConnectorType, result.ExternalId);
        }
        else
        {
            // Calculate next retry time
            var nextDelaySecs = retryPolicy.NextDelaySeconds(job.AttemptCount);
            var nextRetryAt   = nextDelaySecs.HasValue
                ? DateTime.UtcNow.AddSeconds(nextDelaySecs.Value)
                : (DateTime?)null;

            job.MarkFailed(result.FailureReason ?? "Unknown error", nextRetryAt);

            logger.LogWarning(
                "Job {JobId} failed (attempt {Attempt}): {Reason}. NextRetry={NextRetry}",
                job.Id, job.AttemptCount, result.FailureReason, nextRetryAt);
        }
    }
}

/// <summary>
/// Background worker that writes daily JSON exports to Azure Blob Storage.
/// Runs once per day at 02:00 UTC.
/// </summary>
public sealed class BlobExportWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<BlobExportWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BlobExportWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNext0200Utc();
            logger.LogInformation("BlobExportWorker sleeping {Delay} until next 02:00 UTC", delay);

            await Task.Delay(delay, stoppingToken);

            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await RunExportsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "BlobExportWorker unhandled error");
            }
        }
    }

    private async Task RunExportsAsync(CancellationToken ct)
    {
        await using var scope    = scopeFactory.CreateAsyncScope();
        var db                   = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        var tokenStore           = scope.ServiceProvider.GetRequiredService<IConnectorTokenStore>();

        var yesterday = DateTime.UtcNow.Date.AddDays(-1);

        var blobConnectors = await db.Connectors
            .Where(c => c.ConnectorType == ConnectorType.AzureBlobExport
                     && c.Status == ConnectorStatus.Connected)
            .ToListAsync(ct);

        logger.LogInformation("BlobExportWorker exporting {Count} tenant(s) for {Date:yyyy-MM-dd}",
            blobConnectors.Count, yesterday);

        foreach (var connector in blobConnectors)
        {
            try
            {
                await ExportTenantAsync(connector, yesterday, tokenStore, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Blob export failed for tenant {TenantId}", connector.TenantId);
            }
        }
    }

    private async Task ExportTenantAsync(
        ConnectorConfig connector,
        DateTime exportDate,
        IConnectorTokenStore tokenStore,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(connector.KeyVaultSecretName))
        {
            logger.LogWarning("Blob connector {ConnectorId} has no KV secret name", connector.Id);
            return;
        }

        var connectionString = await tokenStore.GetRefreshTokenAsync(connector.KeyVaultSecretName, ct);
        if (string.IsNullOrEmpty(connectionString))
        {
            logger.LogWarning("Blob connection string not found in KV for connector {ConnectorId}", connector.Id);
            return;
        }

        var blobServiceClient  = new Azure.Storage.Blobs.BlobServiceClient(connectionString);
        var containerClient    = blobServiceClient.GetBlobContainerClient("crm-exports");
        await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);

        var blobPath = $"{connector.TenantId}/{exportDate:yyyy}/{exportDate:MM}/{exportDate:dd}/export.json";
        var tempPath = blobPath + ".tmp";

        // Build export payload — placeholder; real implementation queries relevant events
        var exportPayload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            TenantId   = connector.TenantId,
            ExportDate = exportDate.ToString("yyyy-MM-dd"),
            ExportedAt = DateTime.UtcNow,
            Events     = Array.Empty<object>(), // populated by full implementation
        });

        // Write atomically: temp → rename
        var tempBlob = containerClient.GetBlobClient(tempPath);
        using var stream = new MemoryStream(exportPayload);
        await tempBlob.UploadAsync(stream, overwrite: true, cancellationToken: ct);

        var finalBlob = containerClient.GetBlobClient(blobPath);
        await finalBlob.StartCopyFromUriAsync(tempBlob.Uri, cancellationToken: ct);
        await tempBlob.DeleteIfExistsAsync(cancellationToken: ct);

        logger.LogInformation("Blob export completed for tenant {TenantId} date {Date:yyyy-MM-dd}",
            connector.TenantId, exportDate);
    }

    private static TimeSpan TimeUntilNext0200Utc()
    {
        var now    = DateTime.UtcNow;
        var next02 = now.Date.AddHours(2);
        if (next02 <= now) next02 = next02.AddDays(1);
        return next02 - now;
    }
}
