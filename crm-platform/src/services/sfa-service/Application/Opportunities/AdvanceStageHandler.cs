using CrmPlatform.SfaService.Domain.Enums;
using CrmPlatform.SfaService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.SfaService.Application.Opportunities;

public sealed record AdvanceStageCommand(Guid OpportunityId, OpportunityStage NewStage);

public sealed class AdvanceStageHandler(
    SfaDbContext db,
    ServiceBusEventPublisher publisher)
{
    public async Task<Result<bool>> HandleAsync(
        AdvanceStageCommand cmd, CancellationToken ct = default)
    {
        var opp = await db.Opportunities.FirstOrDefaultAsync(o => o.Id == cmd.OpportunityId, ct);

        if (opp is null)
            return Result.Fail<bool>("Opportunity not found.", ResultErrorCode.NotFound);

        try
        {
            opp.AdvanceStage(cmd.NewStage);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail<bool>(ex.Message, ResultErrorCode.ValidationError);
        }

        await db.SaveChangesAsync(ct);

        foreach (var evt in opp.DomainEvents)
            await publisher.PublishAsync("crm.sfa", evt, ct);

        return Result.Ok(true);
    }
}
