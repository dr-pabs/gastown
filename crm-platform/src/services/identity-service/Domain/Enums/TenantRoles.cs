namespace CrmPlatform.IdentityService.Domain.Enums;

/// <summary>
/// Roles that may be stored as UserRole records.
/// PlatformAdmin is intentionally excluded — it is a platform-level claim, not a tenant role.
/// </summary>
public static class TenantRoles
{
    public const string SalesRep       = "SalesRep";
    public const string SalesManager   = "SalesManager";
    public const string SupportAgent   = "SupportAgent";
    public const string SupportManager = "SupportManager";
    public const string MarketingUser  = "MarketingUser";
    public const string TenantAdmin    = "TenantAdmin";

    private static readonly HashSet<string> ValidTenantRoles =
    [
        SalesRep,
        SalesManager,
        SupportAgent,
        SupportManager,
        MarketingUser,
        TenantAdmin,
    ];

    public static bool IsValidTenantRole(string role) =>
        ValidTenantRoles.Contains(role);
}
