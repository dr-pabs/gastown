using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CrmPlatform.PlatformAdminService.Domain.Enums;
using CrmPlatform.PlatformAdminService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.PlatformAdminService.Application.Tenants;

// ─── Suspend ─────────────────────────────────────────────────────────────────

public sealed record SuspendTenantCommand(Guid TenantId, string SuspendedBy);

public sealed class SuspendTenantHandler(
    PlatformDbContext db,
    ServiceBusEventPublisher publisher,
    ILogger<SuspendTenantHandler> logger)
{
    public async Task<Result<bool>> HandleAsync(SuspendTenantCommand command, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == command.TenantId, ct);

        if (tenant is null)
            return Result.Fail<bool>("Tenant not found", ResultErrorCode.NotFound);

        tenant.Suspend(command.SuspendedBy);

        db.ProvisioningLogs.Add(Domain.Entities.TenantProvisioningLog.Write(
            tenant.Id, tenant.Id, "Suspended", ProvisioningStepStatus.Completed,
            details: $"Suspended by {command.SuspendedBy}"));

        await db.SaveChangesAsync(ct);

        foreach (var e in tenant.DomainEvents) await publisher.PublishAsync("crm.platform", e, ct);
        tenant.ClearDomainEvents();

        logger.LogInformation("Tenant {TenantId} suspended by {SuspendedBy}", command.TenantId, command.SuspendedBy);
        return Result.Ok(true);
    }
}

// ─── Reinstate ────────────────────────────────────────────────────────────────

public sealed record ReinstateTenantCommand(Guid TenantId, string ReinstatedBy);

public sealed class ReinstateTenantHandler(
    PlatformDbContext db,
    ServiceBusEventPublisher publisher,
    ILogger<ReinstateTenantHandler> logger)
{
    public async Task<Result<bool>> HandleAsync(ReinstateTenantCommand command, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == command.TenantId, ct);

        if (tenant is null)
            return Result.Fail<bool>("Tenant not found", ResultErrorCode.NotFound);

        tenant.Reinstate(command.ReinstatedBy);

        db.ProvisioningLogs.Add(Domain.Entities.TenantProvisioningLog.Write(
            tenant.Id, tenant.Id, "Reinstated", ProvisioningStepStatus.Completed,
            details: $"Reinstated by {command.ReinstatedBy}"));

        await db.SaveChangesAsync(ct);

        foreach (var e in tenant.DomainEvents) await publisher.PublishAsync("crm.platform", e, ct);
        tenant.ClearDomainEvents();

        logger.LogInformation("Tenant {TenantId} reinstated by {ReinstatedBy}", command.TenantId, command.ReinstatedBy);
        return Result.Ok(true);
    }
}

// ─── Deprovision ─────────────────────────────────────────────────────────────

public sealed record DeprovisionTenantCommand(Guid TenantId, string DeprovisionedBy);

public sealed class DeprovisionTenantHandler(
    PlatformDbContext db,
    ServiceBusEventPublisher publisher,
    ILogger<DeprovisionTenantHandler> logger)
{
    public async Task<Result<bool>> HandleAsync(DeprovisionTenantCommand command, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == command.TenantId, ct);

        if (tenant is null)
            return Result.Fail<bool>("Tenant not found", ResultErrorCode.NotFound);

        tenant.BeginDeprovisioning();
        await db.SaveChangesAsync(ct);

        // All downstream steps publish events — other services react
        tenant.CompleteDeprovisioning(command.DeprovisionedBy);

        db.ProvisioningLogs.Add(Domain.Entities.TenantProvisioningLog.Write(
            tenant.Id, tenant.Id, "Deprovisioned", ProvisioningStepStatus.Completed,
            details: $"Deprovisioned by {command.DeprovisionedBy}"));

        await db.SaveChangesAsync(ct);

        foreach (var e in tenant.DomainEvents) await publisher.PublishAsync("crm.platform", e, ct);
        tenant.ClearDomainEvents();

        logger.LogInformation("Tenant {TenantId} deprovisioned by {By}", command.TenantId, command.DeprovisionedBy);
        return Result.Ok(true);
    }
}

// ─── GDPR Erase ───────────────────────────────────────────────────────────────

public sealed record GdprEraseTenantCommand(Guid TenantId, string RequestedBy);

public sealed class GdprEraseTenantHandler(
    PlatformDbContext db,
    ServiceBusEventPublisher publisher,
    ILogger<GdprEraseTenantHandler> logger)
{
    public async Task<Result<bool>> HandleAsync(GdprEraseTenantCommand command, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == command.TenantId, ct);

        if (tenant is null)
            return Result.Fail<bool>("Tenant not found", ResultErrorCode.NotFound);

        tenant.MarkErased();

        db.ProvisioningLogs.Add(Domain.Entities.TenantProvisioningLog.Write(
            tenant.Id, tenant.Id, "GdprErased", ProvisioningStepStatus.Completed,
            details: $"GDPR erasure requested by {command.RequestedBy}"));

        await db.SaveChangesAsync(ct);

        foreach (var e in tenant.DomainEvents) await publisher.PublishAsync("crm.platform", e, ct);
        tenant.ClearDomainEvents();

        logger.LogInformation("GDPR erasure complete for tenant {TenantId}", command.TenantId);
        return Result.Ok(true);
    }
}
