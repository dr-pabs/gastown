using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CrmPlatform.MarketingService.Domain.Entities;
using CrmPlatform.MarketingService.Domain.Enums;
using CrmPlatform.MarketingService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.MarketingService.Application.Campaigns;

public sealed record CreateCampaignCommand(
    string          Name,
    string          Description,
    CampaignChannel Channel,
    Guid            RequestedByUserId);

public sealed record CreateCampaignResult(Guid CampaignId, string Name);

public sealed class CreateCampaignHandler(
    MarketingDbContext db,
    ITenantContext tenantContext,
    ServiceBusEventPublisher publisher,
    ILogger<CreateCampaignHandler> logger)
{
    public async Task<Result<CreateCampaignResult>> HandleAsync(
        CreateCampaignCommand command,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return Result.Fail<CreateCampaignResult>("Campaign name is required.", ResultErrorCode.ValidationError);

        var campaign = Campaign.Create(
            tenantContext.TenantId,
            command.Name,
            command.Description,
            command.Channel,
            command.RequestedByUserId);

        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(ct);

        foreach (var evt in campaign.DomainEvents)
            await publisher.PublishAsync("crm.marketing", evt, ct);
        campaign.ClearDomainEvents();

        logger.LogInformation(
            "Campaign {CampaignId} created for tenant {TenantId}",
            campaign.Id, tenantContext.TenantId);

        return Result.Ok(new CreateCampaignResult(campaign.Id, campaign.Name));
    }
}
