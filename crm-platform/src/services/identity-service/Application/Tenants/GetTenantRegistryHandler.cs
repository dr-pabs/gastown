using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CrmPlatform.IdentityService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.IdentityService.Application.Tenants;

public sealed record GetTenantRegistryQuery(Guid TenantId);

public sealed record TenantRegistryDto(
    Guid TenantId,
    Guid EntraTenantId,
    Guid ExternalIdTenantId,
    string Status);

public sealed class GetTenantRegistryHandler(
    IdentityDbContext db,
    ILogger<GetTenantRegistryHandler> logger)
{
    public async Task<Result<TenantRegistryDto>> HandleAsync(
        GetTenantRegistryQuery query,
        CancellationToken ct = default)
    {
        var registry = await db.TenantRegistries
            .IgnoreQueryFilters() // internal lookup — bypass tenant filter
            .FirstOrDefaultAsync(r => r.TenantId == query.TenantId, ct);

        if (registry is null)
            return Result.Fail<TenantRegistryDto>(
                $"Tenant {query.TenantId} not found in registry", ResultErrorCode.NotFound);

        var dto = new TenantRegistryDto(
            registry.TenantId,
            registry.EntraTenantId,
            registry.ExternalIdTenantId,
            registry.Status.ToString());

        return Result.Ok(dto);
    }
}
