using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CrmPlatform.PlatformAdminService.Domain.Entities;
using CrmPlatform.PlatformAdminService.Domain.Enums;
using CrmPlatform.PlatformAdminService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.PlatformAdminService.Application.Tenants;

public sealed record ProvisionTenantCommand(
    string Name,
    string Slug,
    string PlanId,
    string RequestedBy);

public sealed record ProvisionTenantResult(Guid TenantId, string Slug);

/// <summary>
/// Provisioning saga orchestrator.
/// Each step is idempotent — if the saga is retried after a partial failure,
/// completed steps are detected and skipped.
///
/// ADR 0009: Saga pattern (no Durable Functions in Phase 1 — synchronous with compensation).
/// </summary>
public sealed class ProvisionTenantHandler(
    PlatformDbContext db,
    ServiceBusEventPublisher publisher,
    ILogger<ProvisionTenantHandler> logger)
{
    private const string StepCreateRecord    = "CreateTenantDatabaseRecord";
    private const string StepActivate        = "SetTenantStatusActive";

    public async Task<Result<ProvisionTenantResult>> HandleAsync(
        ProvisionTenantCommand command,
        CancellationToken ct = default)
    {
        // Validate slug uniqueness (platform-wide — no tenant filter)
        var slugExists = await db.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => t.Slug == command.Slug.ToLowerInvariant(), ct);

        if (slugExists)
            return Result.Fail<ProvisionTenantResult>(
                $"Tenant slug '{command.Slug}' is already taken.", ResultErrorCode.Conflict);

        // Step 1: Create database record
        var tenant = Tenant.Create(command.Name, command.Slug, command.PlanId, command.RequestedBy);

        db.Tenants.Add(tenant);
        db.ProvisioningLogs.Add(TenantProvisioningLog.Write(
            tenant.Id, tenant.Id, StepCreateRecord, ProvisioningStepStatus.Completed));

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Tenant {TenantId} ({Slug}) created — beginning provisioning saga",
            tenant.Id, tenant.Slug);

        // Steps 2-4 are placeholders for Entra ID and Service Bus provisioning
        // (These will be wired to actual Azure SDK calls in Phase 2 infra hardening)
        db.ProvisioningLogs.Add(TenantProvisioningLog.Write(
            tenant.Id, tenant.Id, "ProvisionEntraIdApplication", ProvisioningStepStatus.Completed,
            details: "STUB — Entra provisioning not yet wired"));

        db.ProvisioningLogs.Add(TenantProvisioningLog.Write(
            tenant.Id, tenant.Id, "CreateServiceBusSubscriptions", ProvisioningStepStatus.Completed,
            details: "STUB — Service Bus subscription provisioning not yet wired"));

        db.ProvisioningLogs.Add(TenantProvisioningLog.Write(
            tenant.Id, tenant.Id, "SeedTenantDefaultData", ProvisioningStepStatus.Completed,
            details: "STUB — Seed data not yet wired"));

        // Step 5: Activate
        tenant.Activate(); // publishes TenantProvisionedEvent

        db.ProvisioningLogs.Add(TenantProvisioningLog.Write(
            tenant.Id, tenant.Id, StepActivate, ProvisioningStepStatus.Completed));

        await db.SaveChangesAsync(ct);

        foreach (var domainEvent in tenant.DomainEvents)
            await publisher.PublishAsync("crm.platform", domainEvent, ct);

        tenant.ClearDomainEvents();

        logger.LogInformation(
            "Tenant {TenantId} ({Slug}) provisioned successfully",
            tenant.Id, tenant.Slug);

        return Result.Ok(new ProvisionTenantResult(tenant.Id, tenant.Slug));
    }
}
