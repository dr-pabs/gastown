using CrmPlatform.CssService.Domain.Entities;
using CrmPlatform.CssService.Domain.Enums;
using CrmPlatform.CssService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.CssService.Application.Sla;

public sealed record CreateSlaPolicyCommand(
    string       Name,
    CasePriority Priority,
    int          FirstResponseMinutes,
    int          ResolutionMinutes,
    bool         BusinessHoursOnly);

public sealed class CreateSlaPolicyHandler(
    CssDbContext db,
    ITenantContext tenantContext)
{
    public async Task<Result<Guid>> HandleAsync(
        CreateSlaPolicyCommand cmd, CancellationToken ct = default)
    {
        SlaPolicy policy;
        try
        {
            policy = SlaPolicy.Create(
                tenantContext.TenantId,
                cmd.Name,
                cmd.Priority,
                cmd.FirstResponseMinutes,
                cmd.ResolutionMinutes,
                cmd.BusinessHoursOnly);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Result.Fail<Guid>(ex.Message, ResultErrorCode.ValidationError);
        }

        db.SlaPolicies.Add(policy);
        await db.SaveChangesAsync(ct);
        return Result.Ok(policy.Id);
    }
}
