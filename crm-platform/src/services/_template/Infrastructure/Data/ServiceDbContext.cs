using Microsoft.EntityFrameworkCore;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.ServiceTemplate.Infrastructure.Data;

/// <summary>
/// Base DbContext that all service DbContexts must inherit.
/// Enforces:
///   - Global TenantId query filter on every BaseEntity.
///   - Soft-delete filter (IsDeleted == false).
///   - Interceptors for SESSION_CONTEXT and audit timestamps.
///
/// Service-specific DbContexts:
///   - Override OnModelCreating and call base.OnModelCreating(builder) FIRST.
///   - Register entity configurations with builder.ApplyConfigurationsFromAssembly.
///
/// NEVER bypass the query filter in application code. Use
/// .IgnoreQueryFilters() only in admin/platform-admin-service operations.
/// </summary>
public abstract class ServiceDbContext : DbContext
{
    private readonly ITenantContextAccessor _tenantContextAccessor;

    protected ServiceDbContext(
        DbContextOptions options,
        ITenantContextAccessor tenantContextAccessor)
        : base(options)
    {
        _tenantContextAccessor = tenantContextAccessor;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply global filter to every entity that inherits BaseEntity
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType)) continue;

            // Filter 1: tenant isolation
            // Filter 2: soft delete
            // Both combined as a single expression to avoid EF filter stacking issues.
            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(BuildTenantAndSoftDeleteFilter(entityType.ClrType));
        }
    }

    private System.Linq.Expressions.LambdaExpression BuildTenantAndSoftDeleteFilter(Type entityType)
    {
        var param = System.Linq.Expressions.Expression.Parameter(entityType, "e");
        var tenantId = System.Linq.Expressions.Expression.Property(param, nameof(BaseEntity.TenantId));
        var isDeleted = System.Linq.Expressions.Expression.Property(param, nameof(BaseEntity.IsDeleted));

        // e.TenantId == CurrentTenantId && !e.IsDeleted
        var tenantFilter = System.Linq.Expressions.Expression.Equal(
            tenantId,
            System.Linq.Expressions.Expression.Call(
                typeof(ServiceDbContext),
                nameof(GetCurrentTenantId),
                null,
                System.Linq.Expressions.Expression.Constant(_tenantContextAccessor)));

        var notDeleted = System.Linq.Expressions.Expression.Equal(
            isDeleted,
            System.Linq.Expressions.Expression.Constant(false));

        var combined = System.Linq.Expressions.Expression.AndAlso(tenantFilter, notDeleted);
        return System.Linq.Expressions.Expression.Lambda(combined, param);
    }

    // Called as part of the query filter expression — must be static
    private static Guid GetCurrentTenantId(ITenantContextAccessor accessor) =>
        accessor.TenantId;
}

/// <summary>
/// Accessed by the EF query filter expression. Scoped to the request.
/// Separate from ITenantContext to avoid capturing the middleware-scoped instance
/// directly in a compiled query filter expression.
/// </summary>
public interface ITenantContextAccessor
{
    Guid TenantId { get; }
}

public sealed class TenantContextAccessor(IHttpContextAccessor httpContextAccessor)
    : ITenantContextAccessor
{
    public Guid TenantId =>
        httpContextAccessor.HttpContext?.RequestServices
            .GetRequiredService<MultiTenancy.ITenantContext>().TenantId
        ?? Guid.Empty;
}
