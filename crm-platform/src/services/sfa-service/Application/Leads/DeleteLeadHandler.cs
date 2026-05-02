using CrmPlatform.SfaService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.SfaService.Application.Leads;

public sealed class DeleteLeadHandler(SfaDbContext db)
{
    public async Task<Result<bool>> HandleAsync(Guid leadId, CancellationToken ct = default)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);

        if (lead is null)
            return Result.Fail<bool>("Lead not found.", ResultErrorCode.NotFound);

        lead.Disqualify(); // Disqualify calls SoftDelete internally
        await db.SaveChangesAsync(ct);
        return Result.Ok(true);
    }
}
