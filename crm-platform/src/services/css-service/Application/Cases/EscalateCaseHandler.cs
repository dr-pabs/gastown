using CrmPlatform.CssService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.CssService.Application.Cases;

public sealed record EscalateCaseCommand(
    Guid    CaseId,
    string  Reason,
    Guid?   NewAssigneeId);

public sealed class EscalateCaseHandler(
    CssDbContext db,
    ITenantContext tenantContext,
    ServiceBusEventPublisher publisher)
{
    public async Task<Result<bool>> HandleAsync(
        EscalateCaseCommand cmd, CancellationToken ct = default)
    {
        var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == cmd.CaseId, ct);

        if (c is null)
            return Result.Fail<bool>("Case not found.", ResultErrorCode.NotFound);

        try
        {
            var escalation = c.Escalate(tenantContext.UserId, cmd.Reason, cmd.NewAssigneeId);
            db.EscalationRecords.Add(escalation);
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
