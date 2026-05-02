using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CrmPlatform.MarketingService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.MarketingService.Application.Campaigns;

public sealed record TransitionCampaignCommand(
    Guid   CampaignId,
    string Action,         // "activate" | "pause" | "complete" | "cancel" | "schedule"
    DateTime? ScheduledAt  // required for "schedule"
);

public sealed class TransitionCampaignHandler(
    MarketingDbContext db,
    ITenantContext tenantContext,
    ServiceBusEventPublisher publisher,
    ILogger<TransitionCampaignHandler> logger)
{
    public async Task<Result<bool>> HandleAsync(
        TransitionCampaignCommand command,
        CancellationToken ct = default)
    {
        var campaign = await db.Campaigns
            .FirstOrDefaultAsync(c => c.Id == command.CampaignId, ct);

        if (campaign is null)
            return Result.Fail<bool>("Campaign not found.", ResultErrorCode.NotFound);

        try
        {
            switch (command.Action.ToLowerInvariant())
            {
                case "activate":
                    campaign.Activate();
                    break;
                case "pause":
                    campaign.Pause();
                    break;
                case "complete":
                    campaign.Complete();
                    break;
                case "cancel":
                    campaign.Cancel();
                    break;
                case "schedule":
                    if (command.ScheduledAt is null)
                        return Result.Fail<bool>("ScheduledAt is required for schedule action.", ResultErrorCode.ValidationError);
                    campaign.Schedule(command.ScheduledAt.Value);
                    break;
                default:
                    return Result.Fail<bool>($"Unknown action '{command.Action}'.", ResultErrorCode.ValidationError);
            }
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail<bool>(ex.Message, ResultErrorCode.ValidationError);
        }

        await db.SaveChangesAsync(ct);

        foreach (var evt in campaign.DomainEvents)
            await publisher.PublishAsync("crm.marketing", evt, ct);
        campaign.ClearDomainEvents();

        logger.LogInformation(
            "Campaign {CampaignId} action '{Action}' applied for tenant {TenantId}",
            campaign.Id, command.Action, tenantContext.TenantId);

        return Result.Ok(true);
    }
}
