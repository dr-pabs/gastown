using CrmPlatform.CssService.Domain.Entities;
using CrmPlatform.CssService.Domain.Enums;
using CrmPlatform.CssService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.CssService.Application.Cases;

public sealed record CreateCaseCommand(
    string       Title,
    string       Description,
    CasePriority Priority,
    CaseChannel  Channel,
    Guid?        ContactId,
    Guid?        AccountId);

public sealed class CreateCaseHandler(
    CssDbContext db,
    ITenantContext tenantContext,
    ServiceBusEventPublisher publisher)
{
    public async Task<Result<Guid>> HandleAsync(
        CreateCaseCommand cmd, CancellationToken ct = default)
    {
        var supportCase = Case.Create(
            tenantContext.TenantId,
            cmd.Title,
            cmd.Description,
            cmd.Priority,
            cmd.Channel,
            cmd.ContactId,
            cmd.AccountId,
            tenantContext.UserId);

        // Auto-open: find matching SLA policy and set deadline
        var slaPolicy = await db.SlaPolicies
            .FirstOrDefaultAsync(p => p.Priority == cmd.Priority, ct);

        if (slaPolicy is not null)
            supportCase.Open(slaPolicy.CalculateDeadline(DateTime.UtcNow));

        db.Cases.Add(supportCase);
        await db.SaveChangesAsync(ct);

        foreach (var evt in supportCase.DomainEvents)
            await publisher.PublishAsync("crm.css", evt, ct);

        return Result.Ok(supportCase.Id);
    }
}
