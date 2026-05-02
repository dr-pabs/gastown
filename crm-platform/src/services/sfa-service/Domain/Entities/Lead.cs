using CrmPlatform.SfaService.Domain.Enums;
using CrmPlatform.SfaService.Domain.Events;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.SfaService.Domain.Entities;

/// <summary>
/// A sales lead — the entry point of the SFA pipeline.
/// Score: 0–100. May only be converted once.
/// </summary>
public sealed class Lead : BaseEntity
{
    private Lead() { } // EF Core

    public string     Name             { get; private set; } = string.Empty;
    public string     Email            { get; private set; } = string.Empty;
    public string?    Phone            { get; private set; }
    public string?    Company          { get; private set; }
    public LeadSource Source           { get; private set; }
    public LeadStatus Status           { get; private set; } = LeadStatus.New;
    public int        Score            { get; private set; }  // 0–100
    public Guid?      AssignedToUserId { get; private set; }
    public bool       IsConverted      { get; private set; }
    public DateTime?  ConvertedAt      { get; private set; }
    public Guid?      ConvertedToOpportunityId { get; private set; }

    // Activities are queried directly by RelatedEntityId + RelatedEntityType = "Lead"
    // No EF navigation — Activity is a polymorphic entity shared across Lead/Opportunity/Contact.

    public static Lead Create(
        Guid tenantId,
        string name,
        string email,
        string? phone,
        string? company,
        LeadSource source,
        Guid createdByUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var lead = new Lead
        {
            TenantId  = tenantId,
            Name      = name,
            Email     = email.ToLowerInvariant(),
            Phone     = phone,
            Company   = company,
            Source    = source,
            Status    = LeadStatus.New,
            Score     = 0,
        };

        lead.AddDomainEvent(new LeadCreatedEvent(lead.Id, tenantId, createdByUserId));
        return lead;
    }

    public void Assign(Guid assignedToUserId, Guid assignedByUserId)
    {
        AssignedToUserId = assignedToUserId;
        if (Status == LeadStatus.New) Status = LeadStatus.Contacted;
        AddDomainEvent(new LeadAssignedEvent(Id, TenantId, assignedToUserId, assignedByUserId));
    }

    public void UpdateScore(int newScore)
    {
        if (newScore < 0 || newScore > 100)
            throw new ArgumentOutOfRangeException(nameof(newScore), "Score must be between 0 and 100.");
        Score = newScore;
    }

    public void Qualify()
    {
        if (Status is LeadStatus.Converted or LeadStatus.Disqualified)
            throw new InvalidOperationException($"Cannot qualify a lead in status {Status}.");
        Status = LeadStatus.Qualified;
    }

    public void Disqualify()
    {
        if (Status is LeadStatus.Converted)
            throw new InvalidOperationException("Cannot disqualify a converted lead.");
        Status = LeadStatus.Disqualified;
        SoftDelete();
    }

    /// <summary>Marks lead as converted. Idempotent — throws if already converted.</summary>
    public void MarkConverted(Guid opportunityId)
    {
        if (IsConverted)
            throw new InvalidOperationException("Lead has already been converted.");
        IsConverted                = true;
        ConvertedAt                = DateTime.UtcNow;
        ConvertedToOpportunityId   = opportunityId;
        Status                     = LeadStatus.Converted;
        AddDomainEvent(new LeadConvertedEvent(Id, TenantId, opportunityId));
    }

    public void Update(string? name, string? email, string? phone, string? company)
    {
        if (!string.IsNullOrWhiteSpace(name))    Name    = name;
        if (!string.IsNullOrWhiteSpace(email))   Email   = email.ToLowerInvariant();
        if (phone   != null) Phone   = phone;
        if (company != null) Company = company;
    }
}
