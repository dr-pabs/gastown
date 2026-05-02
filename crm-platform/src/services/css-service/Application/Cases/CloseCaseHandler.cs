using CrmPlatform.CssService.Domain.Enums;
using CrmPlatform.CssService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.CssService.Application.Cases;

public sealed record CloseCaseCommand(Guid CaseId);

public sealed class CloseCaseHandler(
    CssDbContext db,
    ITenantContext tenantContext,
    ServiceBusEventPublisher publisher)
{
    public async Task<Result<bool>> HandleAsync(
        CloseCaseCommand cmd, CancellationToken ct = default)
    {
        var supportCase = await db.Cases.FirstOrDefaultAsync(x => x.Id == cmd.CaseId, ct);

        if (supportCase is null)
            return Result.Fail<bool>("Case not found.", ResultErrorCode.NotFound);

        try
        {
            if (supportCase.Status == CaseStatus.Closed)
                return Result.Ok(true);

            if (supportCase.Status != CaseStatus.Resolved)
                supportCase.Resolve();

            supportCase.Close();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail<bool>(ex.Message, ResultErrorCode.ValidationError);
        }

        await db.SaveChangesAsync(ct);

        foreach (var evt in supportCase.DomainEvents)
            await publisher.PublishAsync("crm.css", evt, ct);

        return Result.Ok(true);
    }
}
