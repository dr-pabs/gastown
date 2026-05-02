using CrmPlatform.SfaService.Domain.Entities;
using CrmPlatform.SfaService.Domain.Enums;
using CrmPlatform.SfaService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.SfaService.Application.Leads;

public sealed record CreateLeadCommand(
    string  Name,
    string  Email,
    string? Phone,
    string? Company,
    LeadSource Source,
    Guid    CreatedByUserId);

public sealed class CreateLeadHandler(
    SfaDbContext db,
    ITenantContext tenantContext,
    ServiceBusEventPublisher publisher)
{
    public async Task<Result<Guid>> HandleAsync(CreateLeadCommand cmd, CancellationToken ct = default)
    {
        var lead = Lead.Create(
            tenantContext.TenantId,
            cmd.Name,
            cmd.Email,
            cmd.Phone,
            cmd.Company,
            cmd.Source,
            cmd.CreatedByUserId);

        db.Leads.Add(lead);
        await db.SaveChangesAsync(ct);

        foreach (var evt in lead.DomainEvents)
            await publisher.PublishAsync("crm.sfa", evt, ct);

        return Result.Ok(lead.Id);
    }
}
