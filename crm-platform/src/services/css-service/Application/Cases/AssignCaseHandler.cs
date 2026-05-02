using CrmPlatform.CssService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.CssService.Application.Cases;

public sealed record AssignCaseCommand(Guid CaseId, Guid AssignedToUserId);

public sealed class AssignCaseHandler(
    CssDbContext db,
    ITenantContext tenantContext,
    ServiceBusEventPublisher publisher)
{
    public async Task<Result<bool>> HandleAsync(
        AssignCaseCommand cmd, CancellationToken ct = default)
    {
        var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == cmd.CaseId, ct);

        if (c is null)
            return Result.Fail<bool>("Case not found.", ResultErrorCode.NotFound);

        try
        {
            c.Assign(cmd.AssignedToUserId, tenantContext.UserId);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail<bool>(ex.Message, ResultErrorCode.ValidationError);
        }

        await db.SaveChangesAsync(ct);

        foreach (var evt in c.DomainEvents)
            await publisher.PublishAsync("crm.css", evt, ct);

        return Result.Ok(true);
    }
}
