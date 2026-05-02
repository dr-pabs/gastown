using CrmPlatform.SfaService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrmPlatform.SfaService.Application.Leads;

/// <summary>
/// Background service that applies score decay once every 24 hours.
/// Leads that have not been updated in more than 24 hours lose 10% of their current score.
/// Minimum score is 0.
/// </summary>
public sealed class ScoreDecayService(
    IServiceScopeFactory scopeFactory,
    ILogger<ScoreDecayService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan InactivityThreshold = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ScoreDecayService started — decay interval {Interval}", Interval);

        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ApplyDecayAsync(stoppingToken);
        }
    }

    private async Task ApplyDecayAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SfaDbContext>();

            var cutoff = DateTime.UtcNow.Subtract(InactivityThreshold);

            // Fetch leads that are still active (not converted/disqualified) and stale
            var staleLeads = await db.Leads
                .Where(l => l.Score > 0
                            && l.UpdatedAt < cutoff
                            && l.Status != Domain.Enums.LeadStatus.Converted
                            && l.Status != Domain.Enums.LeadStatus.Disqualified)
                .ToListAsync(ct);

            foreach (var lead in staleLeads)
            {
                var decayed = Math.Max(0, (int)(lead.Score * 0.9));
                lead.UpdateScore(decayed);
            }

            if (staleLeads.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation(
                    "ScoreDecay: decayed {Count} leads", staleLeads.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ScoreDecayService encountered an error during decay run");
        }
    }
}
