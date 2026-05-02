namespace CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

/// <summary>
/// Per-request tenant context. Populated by TenantContextMiddleware from the
/// validated JWT claims before any handler runs.
/// Injected as scoped — one instance per HTTP request.
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }
    Guid UserId { get; }
    string Role { get; }
    bool IsAuthenticated { get; }
}

public sealed class TenantContext : ITenantContext
{
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string Role { get; private set; } = string.Empty;
    public bool IsAuthenticated { get; private set; }

    internal void SetFromClaims(Guid tenantId, Guid userId, string role)
    {
        TenantId = tenantId;
        UserId = userId;
        Role = role;
        IsAuthenticated = true;
    }
}
