namespace CrmPlatform.PlatformAdminService.Api.Dtos;

// ─── Tenants ──────────────────────────────────────────────────────────────────

public sealed record CreateTenantRequest(string Name, string Slug, string PlanId);

public sealed record UpdateTenantRequest(string? Name, string? PlanId);

public sealed record TenantResponse(
    Guid     Id,
    string   Name,
    string   Slug,
    string   PlanId,
    string   Status,
    DateTime CreatedAt,
    DateTime? SuspendedAt);

public sealed record PagedTenantsResponse(
    IReadOnlyList<TenantResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

// ─── Audit ────────────────────────────────────────────────────────────────────

public sealed record ProvisioningLogResponse(
    Guid     Id,
    string   Step,
    string   StepStatus,
    DateTime OccurredAt,
    string?  Details);

public sealed record SuspendRequest(string Reason);
public sealed record ReinstateRequest(string Reason);
public sealed record GdprEraseRequest(string ConfirmationToken);
