using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CrmPlatform.IdentityService.Domain.Enums;
using CrmPlatform.IdentityService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.IdentityService.Application.Users;

public sealed record DeprovisionUserCommand(
    Guid   TenantId,
    Guid   UserId,
    string InitiatedBy);

public sealed class DeprovisionUserHandler(
    IdentityDbContext db,
    ServiceBusEventPublisher publisher,
    ILogger<DeprovisionUserHandler> logger)
{
    public async Task<Result<bool>> HandleAsync(
        DeprovisionUserCommand command,
        CancellationToken ct = default)
    {
        var user = await db.TenantUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == command.UserId, ct);

        if (user is null)
            return Result.Fail<bool>("User not found", ResultErrorCode.NotFound);

        user.Deprovision(); // soft delete — GDPR hard delete only on tenant.deprovisioned event

        var log = Domain.Entities.UserProvisioningLog.Write(
            command.TenantId,
            user.Id,
            UserProvisioningAction.Deprovisioned,
            command.InitiatedBy);

        db.UserProvisioningLogs.Add(log);
        await db.SaveChangesAsync(ct);

        foreach (var domainEvent in user.DomainEvents)
            await publisher.PublishAsync("crm.identity", domainEvent, ct);

        user.ClearDomainEvents();

        logger.LogInformation(
            "Deprovisioned user {UserId} in tenant {TenantId}",
            command.UserId, command.TenantId);

        return Result.Ok(true);
    }
}
