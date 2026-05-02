namespace CrmPlatform.IdentityService.Domain.Enums;

public enum UserProvisioningAction
{
    Provisioned,
    Deprovisioned,
    RoleGranted,
    RoleRevoked,
    Suspended,
    Reinstated,
    GdprErased,
}
