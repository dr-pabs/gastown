using CrmPlatform.CssService.Domain.Events;
using CrmPlatform.CssService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrmPlatform.CssService.Application.Sla;

/// <summary>
/// Polls every 60 seconds for cases that have breached their SLA deadline (or are at 80% warning).
/// Publishes sla.breached events with Severity = "Warning" or "Breached".
/// Phase 2 implementation — Durable Function upgrade is Phase 3.
/// </summary>
public sealed class SlaMonitorService(
    IServiceScopeFactory scopeFactory,
    ILogger<SlaMonitorService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SlaMonitorService started — poll interval {Interval}", Interval);

        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CheckSlaAsync(stoppingToken);
        }
    }

    private async Task CheckSlaAsync(CancellationToken ct)
    {
        try
        {
            using var scope     = scopeFactory.CreateScope();
            var db              = scope.ServiceProvider.GetRequiredService<CssDbContext>();
            var publisher       = scope.ServiceProvider.GetRequiredService<ServiceBusEventPublisher>();
            var now             = DateTime.UtcNow;

            // Fetch open cases with a SLA deadline that haven't been marked breached yet
            var overdueCases = await db.Cases
                .IgnoreQueryFilters()
                .Where(c =>
                    c.SlaDeadline.HasValue
                    && !c.SlaBreached
                    && !c.IsDeleted
                    && c.Status != Domain.Enums.CaseStatus.Resolved
                    && c.Status != Domain.Enums.CaseStatus.Closed
                    && c.SlaDeadline <= now)
                .ToListAsync(ct);

            foreach (var c in overdueCases)
            {
                c.MarkSlaBreached();

                var evt = new SlaBreachedEvent(
                    c.Id, c.TenantId, "Breached", c.SlaDeadline!.Value, now);

                await publisher.PublishAsync("crm.css", evt, ct);

                logger.LogWarning(
                    "SLA Breached: Case {CaseId} tenant {TenantId} deadline {Deadline}",
                    c.Id, c.TenantId, c.SlaDeadline);
            }

            if (overdueCases.Count > 0)
                await db.SaveChangesAsync(ct);

            // Warning: cases at 80% threshold — not yet breached
            var warningCases = await db.Cases
                .IgnoreQueryFilters()
                .Where(c =>
                    c.SlaDeadline.HasValue
                    && !c.SlaBreached
                    && !c.IsDeleted
                    && c.Status != Domain.Enums.CaseStatus.Resolved
                    && c.Status != Domain.Enums.CaseStatus.Closed
                    && c.SlaDeadline > now)
                .ToListAsync(ct);

            foreach (var c in warningCases)
            {
                if (c.CreatedAt == default || c.SlaDeadline == null) continue;

                var total    = (c.SlaDeadline.Value - c.CreatedAt).TotalSeconds;
                var elapsed  = (now - c.CreatedAt).TotalSeconds;

                if (elapsed / total >= 0.8)
                {
                    var evt = new SlaBreachedEvent(
                        c.Id, c.TenantId, "Warning", c.SlaDeadline!.Value, now);

                    await publisher.PublishAsync("crm.css", evt, ct);

                    logger.LogWarning(
                        "SLA Warning: Case {CaseId} tenant {TenantId} at 80% threshold",
                        c.Id, c.TenantId);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "SlaMonitorService encountered an error during poll");
        }
    }
}
