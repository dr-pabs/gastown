using CrmPlatform.SfaService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.SfaService.Domain.Entities;

/// <summary>
/// Polymorphic activity record — call, email, meeting, or note — linked to any SFA entity.
/// </summary>
public sealed class Activity : BaseEntity
{
    private Activity() { } // EF Core

    public ActivityType ActivityType      { get; private set; }
    public Guid         RelatedEntityId   { get; private set; }
    public string       RelatedEntityType { get; private set; } = string.Empty; // "Lead", "Opportunity", "Contact"
    public DateTime     OccurredAt        { get; private set; }
    public string?      Notes             { get; private set; }
    public Guid         AuthorUserId      { get; private set; }

    public static Activity Record(
        Guid tenantId,
        ActivityType type,
        Guid relatedEntityId,
        string relatedEntityType,
        DateTime occurredAt,
        Guid authorUserId,
        string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relatedEntityType);
        return new Activity
        {
            TenantId          = tenantId,
            ActivityType      = type,
            RelatedEntityId   = relatedEntityId,
            RelatedEntityType = relatedEntityType,
            OccurredAt        = occurredAt,
            AuthorUserId      = authorUserId,
            Notes             = notes,
        };
    }
}
