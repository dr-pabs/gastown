using CrmPlatform.MarketingService.Domain.Enums;
using CrmPlatform.MarketingService.Domain.Events;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.MarketingService.Domain.Entities;

/// <summary>
/// A marketing campaign targeting a set of contacts over a defined period.
/// State machine: Draft → Scheduled → Active → Completed/Cancelled
///                         Active → Paused → Active (resume)
/// </summary>
public sealed class Campaign : BaseEntity
{
    public string          Name        { get; private set; } = string.Empty;
    public string          Description { get; private set; } = string.Empty;
    public CampaignChannel Channel     { get; private set; }
    public CampaignStatus  Status      { get; private set; }
    public DateTime?       ScheduledAt { get; private set; }
    public DateTime?       StartedAt   { get; private set; }
    public DateTime?       EndedAt     { get; private set; }
    public Guid            CreatedByUserId { get; private set; }

    // Navigation
    public IReadOnlyList<Journey> Journeys { get; private set; } = [];

    private Campaign() { } // EF Core

    public static Campaign Create(
        Guid           tenantId,
        string         name,
        string         description,
        CampaignChannel channel,
        Guid           createdByUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var c = new Campaign
        {
            Id              = Guid.NewGuid(),
            TenantId        = tenantId,
            Name            = name.Trim(),
            Description     = description.Trim(),
            Channel         = channel,
            Status          = CampaignStatus.Draft,
            CreatedByUserId = createdByUserId,
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow
        };

        c.AddDomainEvent(new CampaignCreatedEvent(c.Id, tenantId, createdByUserId));
        return c;
    }

    /// <summary>Schedule the campaign for future activation.</summary>
    public void Schedule(DateTime scheduledAt)
    {
        if (Status != CampaignStatus.Draft)
            throw new InvalidOperationException($"Cannot schedule a campaign in status {Status}.");
        if (scheduledAt <= DateTime.UtcNow)
            throw new InvalidOperationException("Scheduled time must be in the future.");

        Status      = CampaignStatus.Scheduled;
        ScheduledAt = scheduledAt;
        UpdatedAt   = DateTime.UtcNow;
    }

    /// <summary>Activate the campaign (sends are live).</summary>
    public void Activate()
    {
        if (Status is not (CampaignStatus.Draft or CampaignStatus.Scheduled or CampaignStatus.Paused))
            throw new InvalidOperationException($"Cannot activate a campaign in status {Status}.");

        Status    = CampaignStatus.Active;
        StartedAt ??= DateTime.UtcNow;
        UpdatedAt   = DateTime.UtcNow;

        AddDomainEvent(new CampaignActivatedEvent(Id, TenantId));
    }

    /// <summary>Pause a running campaign.</summary>
    public void Pause()
    {
        if (Status != CampaignStatus.Active)
            throw new InvalidOperationException($"Cannot pause a campaign in status {Status}.");

        Status    = CampaignStatus.Paused;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Mark the campaign as completed (all sends done).</summary>
    public void Complete()
    {
        if (Status is not (CampaignStatus.Active or CampaignStatus.Paused))
            throw new InvalidOperationException($"Cannot complete a campaign in status {Status}.");

        Status    = CampaignStatus.Completed;
        EndedAt   = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Cancel the campaign at any pre-completed stage.</summary>
    public void Cancel()
    {
        if (Status is CampaignStatus.Completed or CampaignStatus.Cancelled)
            throw new InvalidOperationException($"Cannot cancel a campaign in status {Status}.");

        Status    = CampaignStatus.Cancelled;
        EndedAt   = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDetails(string name, string description)
    {
        if (Status is not (CampaignStatus.Draft or CampaignStatus.Scheduled))
            throw new InvalidOperationException("Can only edit a campaign in Draft or Scheduled status.");

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name        = name.Trim();
        Description = description.Trim();
        UpdatedAt   = DateTime.UtcNow;
    }
}
