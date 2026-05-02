using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CrmPlatform.MarketingService.Domain.Entities;
using CrmPlatform.MarketingService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.MarketingService.Application.Journeys;

public sealed record CreateJourneyCommand(
    Guid   CampaignId,
    string Name,
    string Description,
    Guid   RequestedByUserId);

public sealed record CreateJourneyResult(Guid JourneyId, string Name);

public sealed class CreateJourneyHandler(
    MarketingDbContext db,
    ITenantContext tenantContext,
    ILogger<CreateJourneyHandler> logger)
{
    public async Task<Result<CreateJourneyResult>> HandleAsync(
        CreateJourneyCommand command,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return Result.Fail<CreateJourneyResult>("Journey name is required.", ResultErrorCode.ValidationError);

        var campaignExists = await db.Campaigns
            .AnyAsync(c => c.Id == command.CampaignId, ct);

        if (!campaignExists)
            return Result.Fail<CreateJourneyResult>("Campaign not found.", ResultErrorCode.NotFound);

        var journey = Journey.Create(
            tenantContext.TenantId,
            command.CampaignId,
            command.Name,
            command.Description,
            command.RequestedByUserId);

        db.Journeys.Add(journey);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Journey {JourneyId} created under campaign {CampaignId} for tenant {TenantId}",
            journey.Id, command.CampaignId, tenantContext.TenantId);

        return Result.Ok(new CreateJourneyResult(journey.Id, journey.Name));
    }
}
