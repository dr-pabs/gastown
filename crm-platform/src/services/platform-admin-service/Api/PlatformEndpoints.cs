using Microsoft.EntityFrameworkCore;
using CrmPlatform.PlatformAdminService.Api.Dtos;
using CrmPlatform.PlatformAdminService.Application.Tenants;
using CrmPlatform.PlatformAdminService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.PlatformAdminService.Api;

public static class PlatformEndpoints
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform")
            .WithTags("Platform Admin")
            .RequireAuthorization();

        MapTenantEndpoints(group);

        return app;
    }

    private static void MapTenantEndpoints(RouteGroupBuilder g)
    {
        // GET /tenants — list all
        g.MapGet("/tenants", async (
            int page,
            int pageSize,
            PlatformDbContext db,
            CancellationToken ct) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = db.Tenants.IgnoreQueryFilters().AsNoTracking();
            var total = await query.CountAsync(ct);

            var items = await query
                .OrderBy(t => t.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new TenantResponse(
                    t.Id, t.Name, t.Slug, t.PlanId, t.Status.ToString(), t.CreatedAt, t.SuspendedAt))
                .ToListAsync(ct);

            return Results.Ok(new PagedTenantsResponse(items, total, page, pageSize));
        })
        .WithName("ListTenants")
        .Produces<PagedTenantsResponse>();

        // POST /tenants — provision
        g.MapPost("/tenants", async (
            CreateTenantRequest request,
            ITenantContext tenantContext,
            ProvisionTenantHandler handler,
            CancellationToken ct) =>
        {
            var command = new ProvisionTenantCommand(
                request.Name, request.Slug, request.PlanId, tenantContext.UserId.ToString());

            var result = await handler.HandleAsync(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/platform/tenants/{result.Value!.TenantId}", result.Value)
                : result.Error?.Code == ResultErrorCode.Conflict
                    ? Results.Conflict(result.Error.Message)
                    : Results.Problem(result.Error!.Message, statusCode: 422);
        })
        .WithName("ProvisionTenant")
        .Produces<ProvisionTenantResult>(StatusCodes.Status201Created);

        // GET /tenants/{id}
        g.MapGet("/tenants/{id:guid}", async (
            Guid id,
            PlatformDbContext db,
            CancellationToken ct) =>
        {
            var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id, ct);

            return tenant is null
                ? Results.NotFound()
                : Results.Ok(new TenantResponse(
                    tenant.Id, tenant.Name, tenant.Slug, tenant.PlanId,
                    tenant.Status.ToString(), tenant.CreatedAt, tenant.SuspendedAt));
        })
        .WithName("GetTenant")
        .Produces<TenantResponse>();

        // PATCH /tenants/{id}
        g.MapPatch("/tenants/{id:guid}", async (
            Guid id,
            UpdateTenantRequest request,
            PlatformDbContext db,
            CancellationToken ct) =>
        {
            var tenant = await db.Tenants.IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == id, ct);

            if (tenant is null) return Results.NotFound();

            tenant.UpdateMetadata(request.Name, request.PlanId);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new TenantResponse(
                tenant.Id, tenant.Name, tenant.Slug, tenant.PlanId,
                tenant.Status.ToString(), tenant.CreatedAt, tenant.SuspendedAt));
        })
        .WithName("UpdateTenant")
        .Produces<TenantResponse>();

        // POST /tenants/{id}/suspend
        g.MapPost("/tenants/{id:guid}/suspend", async (
            Guid id,
            SuspendRequest request,
            ITenantContext tenantContext,
            SuspendTenantHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new SuspendTenantCommand(id, tenantContext.UserId.ToString()), ct);
            return result.IsSuccess ? Results.Accepted()
                : result.Error?.Code == ResultErrorCode.NotFound ? Results.NotFound()
                : Results.Problem(result.Error!.Message, statusCode: 422);
        })
        .WithName("SuspendTenant")
        .Produces(StatusCodes.Status202Accepted);

        // POST /tenants/{id}/reinstate
        g.MapPost("/tenants/{id:guid}/reinstate", async (
            Guid id,
            ReinstateRequest request,
            ITenantContext tenantContext,
            ReinstateTenantHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new ReinstateTenantCommand(id, tenantContext.UserId.ToString()), ct);
            return result.IsSuccess ? Results.Accepted()
                : result.Error?.Code == ResultErrorCode.NotFound ? Results.NotFound()
                : Results.Problem(result.Error!.Message, statusCode: 422);
        })
        .WithName("ReinstateTenant")
        .Produces(StatusCodes.Status202Accepted);

        // DELETE /tenants/{id} — deprovision
        g.MapDelete("/tenants/{id:guid}", async (
            Guid id,
            ITenantContext tenantContext,
            DeprovisionTenantHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new DeprovisionTenantCommand(id, tenantContext.UserId.ToString()), ct);
            return result.IsSuccess ? Results.Accepted()
                : result.Error?.Code == ResultErrorCode.NotFound ? Results.NotFound()
                : Results.Problem(result.Error!.Message, statusCode: 422);
        })
        .WithName("DeprovisionTenant")
        .Produces(StatusCodes.Status202Accepted);

        // POST /tenants/{id}/gdpr-erase
        g.MapPost("/tenants/{id:guid}/gdpr-erase", async (
            Guid id,
            GdprEraseRequest request,
            ITenantContext tenantContext,
            GdprEraseTenantHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new GdprEraseTenantCommand(id, tenantContext.UserId.ToString()), ct);
            return result.IsSuccess ? Results.Accepted()
                : result.Error?.Code == ResultErrorCode.NotFound ? Results.NotFound()
                : Results.Problem(result.Error!.Message, statusCode: 422);
        })
        .WithName("GdprEraseTenant")
        .Produces(StatusCodes.Status202Accepted);

        // GET /tenants/{id}/audit
        g.MapGet("/tenants/{id:guid}/audit", async (
            Guid id,
            PlatformDbContext db,
            CancellationToken ct) =>
        {
            var logs = await db.ProvisioningLogs.IgnoreQueryFilters()
                .Where(l => l.TenantEntityId == id)
                .OrderBy(l => l.OccurredAt)
                .Select(l => new ProvisioningLogResponse(
                    l.Id, l.Step, l.StepStatus.ToString(), l.OccurredAt, l.Details))
                .ToListAsync(ct);

            return Results.Ok(logs);
        })
        .WithName("GetTenantAuditLog")
        .Produces<IReadOnlyList<ProvisioningLogResponse>>();
    }
}
