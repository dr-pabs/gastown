namespace CrmPlatform.ServiceTemplate.Domain;

/// <summary>
/// Base class for all CRM domain entities.
/// Rules (from CLAUDE.md):
///   - Every entity has TenantId — no exceptions.
///   - IsDeleted drives soft delete — never hard delete domain data.
///   - CreatedAt/UpdatedAt set by DbContext SaveChanges intercept.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    /// <summary>Tenant this record belongs to. Enforced by RLS + EF HasQueryFilter.</summary>
    public Guid TenantId { get; protected set; }

    /// <summary>Soft-delete flag. Filtered from all queries by default.</summary>
    public bool IsDeleted { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    // ─── Domain events ────────────────────────────────────────────────────────
    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) =>
        _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();

    // ─── Lifecycle ────────────────────────────────────────────────────────────
    internal void SetCreatedAt(DateTime utcNow) => CreatedAt = utcNow;
    internal void SetUpdatedAt(DateTime utcNow) => UpdatedAt = utcNow;

    public void SoftDelete()
    {
        if (IsDeleted) return;
        IsDeleted = true;
        AddDomainEvent(new EntitySoftDeletedEvent(GetType().Name, Id, TenantId));
    }
}

/// <summary>Marker interface for domain events published via Service Bus.</summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }
    Guid TenantId { get; }
    string EventType { get; }
}

/// <summary>Base implementation of IDomainEvent.</summary>
public abstract record DomainEvent(Guid TenantId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public abstract string EventType { get; }
}

internal record EntitySoftDeletedEvent(string EntityType, Guid EntityId, Guid TenantId)
    : DomainEvent(TenantId)
{
    public override string EventType => $"{EntityType}.Deleted";
}
