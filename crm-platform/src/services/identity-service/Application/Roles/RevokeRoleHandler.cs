using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CrmPlatform.IdentityService.Domain.Enums;
using CrmPlatform.IdentityService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.IdentityService.Application.Roles;

public sealed record RevokeRoleCommand(
    Guid   TenantId,
    Guid   UserId,
    string Role,
    string RevokedBy);

public sealed class RevokeRoleHandler(
    IdentityDbContext db,
    ServiceBusEventPublisher publisher,
    ILogger<RevokeRoleHandler> logger)
{
    public async Task<Result<bool>> HandleAsync(
        RevokeRoleCommand command,
        CancellationToken ct = default)
    {
        var userRole = await db.UserRoles
            .FirstOrDefaultAsync(
                r => r.TenantUserId == command.UserId && r.Role == command.Role, ct);

        if (userRole is null)
            return Result.Fail<bool>($"Role '{command.Role}' not found for user {command.UserId}", ResultErrorCode.NotFound);

        userRole.Revoke(command.RevokedBy);

        var log = Domain.Entities.UserProvisioningLog.Write(
            command.TenantId,
            command.UserId,
            UserProvisioningAction.RoleRevoked,
            command.RevokedBy,
            details: command.Role);

        db.UserProvisioningLogs.Add(log);
        await db.SaveChangesAsync(ct);

        foreach (var domainEvent in userRole.DomainEvents)
            await publisher.PublishAsync("crm.identity", domainEvent, ct);

        userRole.ClearDomainEvents();

        logger.LogInformation(
            "Revoked role {Role} from user {UserId} in tenant {TenantId}",
            command.Role, command.UserId, command.TenantId);

        return Result.Ok(true);
    }
}
