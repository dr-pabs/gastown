using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CrmPlatform.MarketingService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.MarketingService.Application.Journeys;

public sealed record SetJourneyStepsCommand(
    Guid   JourneyId,
    string StepsJson,
    int    StepCount);

public sealed record PublishJourneyCommand(Guid JourneyId);

public sealed class PublishJourneyHandler(
    MarketingDbContext db,
    ITenantContext tenantContext,
    ServiceBusEventPublisher publisher,
    ILogger<PublishJourneyHandler> logger)
{
    public async Task<Result<bool>> HandleSetStepsAsync(
        SetJourneyStepsCommand command,
        CancellationToken ct = default)
    {
        var journey = await db.Journeys
            .FirstOrDefaultAsync(j => j.Id == command.JourneyId, ct);

        if (journey is null)
            return Result.Fail<bool>("Journey not found.", ResultErrorCode.NotFound);

        try
        {
            journey.SetSteps(command.StepsJson, command.StepCount);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail<bool>(ex.Message, ResultErrorCode.ValidationError);
        }

        await db.SaveChangesAsync(ct);
        return Result.Ok(true);
    }

    public async Task<Result<bool>> HandlePublishAsync(
        PublishJourneyCommand command,
        CancellationToken ct = default)
    {
        var journey = await db.Journeys
            .FirstOrDefaultAsync(j => j.Id == command.JourneyId, ct);

        if (journey is null)
            return Result.Fail<bool>("Journey not found.", ResultErrorCode.NotFound);

        try
        {
            journey.Publish();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail<bool>(ex.Message, ResultErrorCode.ValidationError);
        }

        await db.SaveChangesAsync(ct);

        foreach (var evt in journey.DomainEvents)
            await publisher.PublishAsync("crm.marketing", evt, ct);
        journey.ClearDomainEvents();

        logger.LogInformation(
            "Journey {JourneyId} published for tenant {TenantId}",
            journey.Id, tenantContext.TenantId);

        return Result.Ok(true);
    }
}
