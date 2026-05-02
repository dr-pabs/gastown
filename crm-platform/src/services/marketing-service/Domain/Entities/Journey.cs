using CrmPlatform.MarketingService.Domain.Events;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.MarketingService.Domain.Entities;

/// <summary>
/// An automated multi-step journey tied to a Campaign.
/// The journey engine (Durable Function) drives enrollments through the steps.
/// A Journey is immutable once published — create a new version instead.
/// </summary>
public sealed class Journey : BaseEntity
{
    public Guid    CampaignId   { get; private set; }
    public string  Name         { get; private set; } = string.Empty;
    public string  Description  { get; private set; } = string.Empty;
    public bool    IsPublished  { get; private set; }
    public int     StepCount    { get; private set; }
    public Guid    CreatedByUserId { get; private set; }

    /// <summary>
    /// JSON-serialised step definitions loaded by the Durable Function orchestrator.
    /// Stored as a string — the domain entity does not interpret step structure.
    /// </summary>
    public string StepsJson { get; private set; } = "[]";

    // Navigation
    public Campaign?                     Campaign    { get; private set; }
    public IReadOnlyList<JourneyEnrollment> Enrollments { get; private set; } = [];

    private Journey() { } // EF Core

    public static Journey Create(
        Guid   tenantId,
        Guid   campaignId,
        string name,
        string description,
        Guid   createdByUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Journey
        {
            Id              = Guid.NewGuid(),
            TenantId        = tenantId,
            CampaignId      = campaignId,
            Name            = name.Trim(),
            Description     = description.Trim(),
            IsPublished     = false,
            StepCount       = 0,
            StepsJson       = "[]",
            CreatedByUserId = createdByUserId,
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow
        };
    }

    /// <summary>Set the journey steps definition (JSON). Only allowed before publishing.</summary>
    public void SetSteps(string stepsJson, int stepCount)
    {
        if (IsPublished)
            throw new InvalidOperationException("Cannot modify a published journey. Create a new version.");
        ArgumentException.ThrowIfNullOrWhiteSpace(stepsJson);
        if (stepCount < 1)
            throw new ArgumentOutOfRangeException(nameof(stepCount), "Journey must have at least one step.");

        StepsJson  = stepsJson;
        StepCount  = stepCount;
        UpdatedAt  = DateTime.UtcNow;
    }

    /// <summary>
    /// Publish the journey — makes it available for new enrollments.
    /// Once published, steps are frozen.
    /// </summary>
    public void Publish()
    {
        if (IsPublished)
            throw new InvalidOperationException("Journey is already published.");
        if (StepCount < 1)
            throw new InvalidOperationException("Journey must have at least one step before publishing.");

        IsPublished = true;
        UpdatedAt   = DateTime.UtcNow;

        AddDomainEvent(new JourneyPublishedEvent(Id, TenantId, CampaignId, StepCount));
    }
}
