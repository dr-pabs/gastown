using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using System.Data.Common;

namespace CrmPlatform.ServiceTemplate.Infrastructure.Data;

/// <summary>
/// EF Core SaveChanges interceptor that:
///   1. Sets SESSION_CONTEXT(N'TenantId', ...) before every command batch so
///      the SQL RLS predicate function fn_TenantFilter can read it.
///   2. Auto-populates BaseEntity.CreatedAt and UpdatedAt.
///
/// Registered in ServiceDbContext via AddInterceptors(...).
/// </summary>
public sealed class TenantSessionContextInterceptor(ITenantContext tenantContext)
    : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        SetTenantSessionContext(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        SetTenantSessionContext(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        SetTenantSessionContext(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        SetTenantSessionContext(command);
        return ValueTask.FromResult(result);
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private void SetTenantSessionContext(DbCommand command)
    {
        if (!tenantContext.IsAuthenticated) return;

        // Prepend SESSION_CONTEXT to the batch so RLS can filter.
        // The stored proc sp_SetTenantContext wraps this safely.
        command.CommandText =
            $"EXEC sp_SetTenantContext @TenantId = '{tenantContext.TenantId}';\n"
            + command.CommandText;
    }
}

/// <summary>
/// EF Core SaveChanges interceptor that auto-populates BaseEntity audit fields.
/// </summary>
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        SetAuditFields(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        SetAuditFields(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void SetAuditFields(DbContext? context)
    {
        if (context is null) return;
        var now = DateTime.UtcNow;
        foreach (var entry in context.ChangeTracker.Entries<Domain.BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.SetCreatedAt(now);
                entry.Entity.SetUpdatedAt(now);
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.SetUpdatedAt(now);
            }
        }
    }
}
