using CrmPlatform.IdentityService.Domain.Enums;

namespace CrmPlatform.IdentityService.Api.Dtos;

// ─── Users ────────────────────────────────────────────────────────────────────

public sealed record ProvisionUserRequest(
    string EntraObjectId,
    string Email,
    string DisplayName);

public sealed record UserResponse(
    Guid   Id,
    Guid   TenantId,
    string EntraObjectId,
    string Email,
    string DisplayName,
    string Status,
    DateTime CreatedAt);

public sealed record PagedUsersResponse(
    IReadOnlyList<UserResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

// ─── Roles ────────────────────────────────────────────────────────────────────

public sealed record GrantRoleRequest(string Role);

public sealed record RoleResponse(
    Guid   Id,
    string Role,
    DateTime GrantedAt,
    string GrantedBy);

// ─── Consent ─────────────────────────────────────────────────────────────────

public sealed record RecordConsentRequest(ConsentType ConsentType);

public sealed record ConsentResponse(Guid ConsentRecordId);

// ─── Tenant Registry ─────────────────────────────────────────────────────────

public sealed record TenantRegistryResponse(
    Guid   TenantId,
    Guid   EntraTenantId,
    Guid   ExternalIdTenantId,
    string Status);
