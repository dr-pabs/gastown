using CrmPlatform.SfaService.Domain.Entities;
using CrmPlatform.SfaService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.SfaService.Application.Opportunities;

public sealed record CreateOpportunityCommand(
    string           Title,
    decimal          Value,
    Guid?            ContactId,
    Guid?            AccountId,
    Guid?            AssignedToUserId,
    DateTime?        CloseDate);

public sealed class CreateOpportunityHandler(
    SfaDbContext db,
    ITenantContext tenantContext)
{
    public async Task<Result<Guid>> HandleAsync(
        CreateOpportunityCommand cmd, CancellationToken ct = default)
    {
        var opp = Opportunity.Create(
            tenantContext.TenantId,
            cmd.Title,
            cmd.Value,
            cmd.ContactId,
            cmd.AccountId,
            cmd.AssignedToUserId);

        if (cmd.CloseDate.HasValue)
            opp.UpdateDetails(cmd.Title, cmd.Value, cmd.CloseDate);

        db.Opportunities.Add(opp);
        await db.SaveChangesAsync(ct);
        return Result.Ok(opp.Id);
    }
}
