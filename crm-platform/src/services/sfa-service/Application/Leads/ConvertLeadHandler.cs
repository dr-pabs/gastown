using CrmPlatform.SfaService.Domain.Entities;
using CrmPlatform.SfaService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.SfaService.Application.Leads;

public sealed record ConvertLeadCommand(
    Guid    LeadId,
    string  OpportunityTitle,
    decimal OpportunityValue,
    Guid?   ContactId,
    Guid?   AccountId,
    Guid?   AssignedToUserId);

public sealed record ConvertLeadResult(Guid OpportunityId);

/// <summary>
/// Converts a lead to an opportunity in a single unit of work.
/// Returns 422 Conflict if the lead is already converted (idempotency guard).
/// </summary>
public sealed class ConvertLeadHandler(
    SfaDbContext db,
    ITenantContext tenantContext,
    ServiceBusEventPublisher publisher)
{
    public async Task<Result<ConvertLeadResult>> HandleAsync(
        ConvertLeadCommand cmd, CancellationToken ct = default)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == cmd.LeadId, ct);

        if (lead is null)
            return Result.Fail<ConvertLeadResult>("Lead not found.", ResultErrorCode.NotFound);

        if (lead.IsConverted)
            return Result.Fail<ConvertLeadResult>(
                $"Lead {cmd.LeadId} has already been converted.", ResultErrorCode.Conflict);

        // Create the opportunity first so we can pass its ID to MarkConverted
        var opportunity = Opportunity.Create(
            tenantContext.TenantId,
            cmd.OpportunityTitle,
            cmd.OpportunityValue,
            cmd.ContactId,
            cmd.AccountId,
            cmd.AssignedToUserId,
            convertedFromLeadId: cmd.LeadId);

        db.Opportunities.Add(opportunity);

        // Mark lead converted — fires LeadConvertedEvent
        lead.MarkConverted(opportunity.Id);

        // Single SaveChanges — atomically creates the opportunity and marks the lead
        await db.SaveChangesAsync(ct);

        // Publish all events from both aggregates
        foreach (var evt in lead.DomainEvents)
            await publisher.PublishAsync("crm.sfa", evt, ct);

        foreach (var evt in opportunity.DomainEvents)
            await publisher.PublishAsync("crm.sfa", evt, ct);

        return Result.Ok(new ConvertLeadResult(opportunity.Id));
    }
}
