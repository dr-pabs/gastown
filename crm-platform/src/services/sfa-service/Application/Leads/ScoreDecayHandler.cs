using CrmPlatform.SfaService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrmPlatform.SfaService.Application.Leads;

/// <summary>
/// Scoped handler that applies lead score decay on-demand.
/// Used by the internal endpoint called from the lead-score-decay Durable Function.
/// The <see cref="ScoreDecayService"/> BackgroundService also delegates to this logic
/// (kept separate to support both scheduled and externally-triggered execution).
/// </summary>
public sealed class ScoreDecayHandler(
    SfaDbContext db,
    ILogger<ScoreDecayHandler> logger)
{
    private static readonly TimeSpan InactivityThreshold = TimeSpan.FromDays(30);

    public async Task<Result<int>> HandleAsync(CancellationToken ct = default)
    {
        try
        {
            var cutoff = DateTime.UtcNow.Subtract(InactivityThreshold);

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
                await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "ScoreDecayHandler: decayed {Count} leads (inactivity > {Days} days)",
                staleLeads.Count, InactivityThreshold.Days);

            return Result.Ok(staleLeads.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ScoreDecayHandler failed");
            return Result.Fail<int>("Lead score decay failed.", ResultErrorCode.General);
        }
    }
}
