using CrmPlatform.SfaService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.SfaService.Application.Leads;

public sealed record AssignLeadCommand(
    Guid AssignedToUserId,
    Guid AssignedByUserId,
    Guid LeadId);

public sealed class AssignLeadHandler(
    SfaDbContext db,
    ServiceBusEventPublisher publisher)
{
    public async Task<Result<bool>> HandleAsync(AssignLeadCommand cmd, CancellationToken ct = default)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == cmd.LeadId, ct);

        if (lead is null)
            return Result.Fail<bool>("Lead not found.", ResultErrorCode.NotFound);

        lead.Assign(cmd.AssignedToUserId, cmd.AssignedByUserId);
        await db.SaveChangesAsync(ct);

        foreach (var evt in lead.DomainEvents)
            await publisher.PublishAsync("crm.sfa", evt, ct);

        return Result.Ok(true);
    }
}
