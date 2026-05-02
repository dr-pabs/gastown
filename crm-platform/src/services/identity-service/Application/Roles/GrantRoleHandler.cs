using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CrmPlatform.IdentityService.Domain.Entities;
using CrmPlatform.IdentityService.Domain.Enums;
using CrmPlatform.IdentityService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.IdentityService.Application.Roles;

public sealed record GrantRoleCommand(
    Guid   TenantId,
    Guid   UserId,
    string Role,
    string GrantedBy);

public sealed record GrantRoleResult(Guid RoleId);

public sealed class GrantRoleHandler(
    IdentityDbContext db,
    ServiceBusEventPublisher publisher,
    ILogger<GrantRoleHandler> logger)
{
    public async Task<Result<GrantRoleResult>> HandleAsync(
        GrantRoleCommand command,
        CancellationToken ct = default)
    {
        // Rule: PlatformAdmin may never be stored as a tenant UserRole
        if (!TenantRoles.IsValidTenantRole(command.Role))
            return Result.Fail<GrantRoleResult>(
                $"'{command.Role}' is not a valid tenant role. PlatformAdmin is a platform-level claim only.",
                ResultErrorCode.ValidationError);

        var user = await db.TenantUsers
            .FirstOrDefaultAsync(u => u.Id == command.UserId, ct);

        if (user is null)
            return Result.Fail<GrantRoleResult>($"User {command.UserId} not found", ResultErrorCode.NotFound);

        // Idempotency: check role already active
        var existing = await db.UserRoles
            .FirstOrDefaultAsync(r => r.TenantUserId == command.UserId && r.Role == command.Role, ct);

        if (existing is not null)
            return Result.Ok(new GrantRoleResult(existing.Id));

        var userRole = UserRole.Grant(command.TenantId, command.UserId, command.Role, command.GrantedBy);

        var log = Domain.Entities.UserProvisioningLog.Write(
            command.TenantId,
            command.UserId,
            UserProvisioningAction.RoleGranted,
            command.GrantedBy,
            details: command.Role);

        db.UserRoles.Add(userRole);
        db.UserProvisioningLogs.Add(log);
        await db.SaveChangesAsync(ct);

        foreach (var domainEvent in userRole.DomainEvents)
            await publisher.PublishAsync("crm.identity", domainEvent, ct);

        userRole.ClearDomainEvents();

        logger.LogInformation(
            "Granted role {Role} to user {UserId} in tenant {TenantId}",
            command.Role, command.UserId, command.TenantId);

        return Result.Ok(new GrantRoleResult(userRole.Id));
    }
}
