using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CrmPlatform.IdentityService.Domain.Entities;
using CrmPlatform.IdentityService.Domain.Enums;
using CrmPlatform.IdentityService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.IdentityService.Application.Users;

public sealed record ProvisionUserCommand(
    Guid   TenantId,
    string EntraObjectId,
    string Email,
    string DisplayName,
    string InitiatedBy);

public sealed record ProvisionUserResult(Guid UserId);

public sealed class ProvisionUserHandler(
    IdentityDbContext db,
    ServiceBusEventPublisher publisher,
    ILogger<ProvisionUserHandler> logger)
{
    public async Task<Result<ProvisionUserResult>> HandleAsync(
        ProvisionUserCommand command,
        CancellationToken ct = default)
    {
        // Idempotency: check by EntraObjectId within tenant
        var existing = await db.TenantUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                u => u.TenantId == command.TenantId && u.EntraObjectId == command.EntraObjectId, ct);

        if (existing is not null)
        {
            logger.LogInformation(
                "User {EntraObjectId} already provisioned in tenant {TenantId} — returning existing",
                command.EntraObjectId, command.TenantId);
            return Result.Ok(new ProvisionUserResult(existing.Id));
        }

        var user = TenantUser.Create(
            command.TenantId,
            command.EntraObjectId,
            command.Email,
            command.DisplayName);

        var log = UserProvisioningLog.Write(
            command.TenantId,
            user.Id,
            UserProvisioningAction.Provisioned,
            command.InitiatedBy);

        db.TenantUsers.Add(user);
        db.UserProvisioningLogs.Add(log);
        await db.SaveChangesAsync(ct);

        foreach (var domainEvent in user.DomainEvents)
            await publisher.PublishAsync("crm.identity", domainEvent, ct);

        user.ClearDomainEvents();

        logger.LogInformation(
            "Provisioned user {UserId} ({Email}) in tenant {TenantId}",
            user.Id, command.Email, command.TenantId);

        return Result.Ok(new ProvisionUserResult(user.Id));
    }
}
