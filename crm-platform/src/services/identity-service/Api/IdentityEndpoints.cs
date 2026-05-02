using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CrmPlatform.IdentityService.Api.Dtos;
using CrmPlatform.IdentityService.Application.Consent;
using CrmPlatform.IdentityService.Application.Roles;
using CrmPlatform.IdentityService.Application.Tenants;
using CrmPlatform.IdentityService.Application.Users;
using CrmPlatform.IdentityService.Domain.Enums;
using CrmPlatform.IdentityService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.IdentityService.Api;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/identity")
            .WithTags("Identity")
            .RequireAuthorization();

        MapUserEndpoints(group);
        MapRoleEndpoints(group);
        MapTenantEndpoints(group);
        MapConsentEndpoints(group);

        return app;
    }

    // ─── Users ────────────────────────────────────────────────────────────────

    private static void MapUserEndpoints(RouteGroupBuilder g)
    {
        g.MapGet("/users", async (
            [FromQuery] int page,
            [FromQuery] int pageSize,
            IdentityDbContext db,
            CancellationToken ct) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = db.TenantUsers.AsNoTracking();
            var total = await query.CountAsync(ct);

            var users = await query
                .OrderBy(u => u.DisplayName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new UserResponse(
                    u.Id, u.TenantId, u.EntraObjectId, u.Email, u.DisplayName,
                    u.Status.ToString(), u.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(new PagedUsersResponse(users, total, page, pageSize));
        })
        .WithName("ListUsers")
        .Produces<PagedUsersResponse>();

        g.MapPost("/users", async (
            ProvisionUserRequest request,
            ITenantContext tenantContext,
            ProvisionUserHandler handler,
            CancellationToken ct) =>
        {
            var command = new ProvisionUserCommand(
                tenantContext.TenantId,
                request.EntraObjectId,
                request.Email,
                request.DisplayName,
                tenantContext.UserId.ToString());

            var result = await handler.HandleAsync(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/identity/users/{result.Value!.UserId}", result.Value)
                : Results.Problem(result.Error!.Message, statusCode: 422);
        })
        .WithName("ProvisionUser")
        .Produces<ProvisionUserResult>(StatusCodes.Status201Created);

        g.MapDelete("/users/{id:guid}", async (
            Guid id,
            ITenantContext tenantContext,
            DeprovisionUserHandler handler,
            CancellationToken ct) =>
        {
            var command = new DeprovisionUserCommand(
                tenantContext.TenantId,
                id,
                tenantContext.UserId.ToString());

            var result = await handler.HandleAsync(command, ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error?.Code == ResultErrorCode.NotFound
                    ? Results.NotFound()
                    : Results.Problem(result.Error!.Message, statusCode: 422);
        })
        .WithName("DeprovisionUser")
        .Produces(StatusCodes.Status204NoContent);
    }

    // ─── Roles ────────────────────────────────────────────────────────────────

    private static void MapRoleEndpoints(RouteGroupBuilder g)
    {
        g.MapGet("/users/{id:guid}/roles", async (
            Guid id,
            IdentityDbContext db,
            CancellationToken ct) =>
        {
            var roles = await db.UserRoles
                .AsNoTracking()
                .Where(r => r.TenantUserId == id)
                .Select(r => new RoleResponse(r.Id, r.Role, r.GrantedAt, r.GrantedBy))
                .ToListAsync(ct);

            return Results.Ok(roles);
        })
        .WithName("ListUserRoles")
        .Produces<IReadOnlyList<RoleResponse>>();

        g.MapPost("/users/{id:guid}/roles", async (
            Guid id,
            GrantRoleRequest request,
            ITenantContext tenantContext,
            GrantRoleHandler handler,
            CancellationToken ct) =>
        {
            var command = new GrantRoleCommand(
                tenantContext.TenantId,
                id,
                request.Role,
                tenantContext.UserId.ToString());

            var result = await handler.HandleAsync(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/identity/users/{id}/roles/{result.Value!.RoleId}", result.Value)
                : result.Error?.Code == ResultErrorCode.NotFound
                    ? Results.NotFound()
                    : Results.Problem(result.Error!.Message, statusCode: 422);
        })
        .WithName("GrantRole")
        .Produces<GrantRoleResult>(StatusCodes.Status201Created);

        g.MapDelete("/users/{id:guid}/roles/{role}", async (
            Guid id,
            string role,
            ITenantContext tenantContext,
            RevokeRoleHandler handler,
            CancellationToken ct) =>
        {
            var command = new RevokeRoleCommand(
                tenantContext.TenantId,
                id,
                role,
                tenantContext.UserId.ToString());

            var result = await handler.HandleAsync(command, ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error?.Code == ResultErrorCode.NotFound
                    ? Results.NotFound()
                    : Results.Problem(result.Error!.Message, statusCode: 422);
        })
        .WithName("RevokeRole")
        .Produces(StatusCodes.Status204NoContent);
    }

    // ─── Tenant Registry (internal) ───────────────────────────────────────────

    private static void MapTenantEndpoints(RouteGroupBuilder g)
    {
        // Internal-only: no APIM public route. Called by middleware tenant-lookup.
        g.MapGet("/tenants/{tenantId:guid}", async (
            Guid tenantId,
            GetTenantRegistryHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetTenantRegistryQuery(tenantId), ct);
            return result.IsSuccess
                ? Results.Ok(new TenantRegistryResponse(
                    result.Value!.TenantId,
                    result.Value.EntraTenantId,
                    result.Value.ExternalIdTenantId,
                    result.Value.Status))
                : Results.NotFound();
        })
        .WithName("GetTenantRegistry")
        .Produces<TenantRegistryResponse>()
        .ExcludeFromDescription(); // hidden from public Swagger
    }

    // ─── Consent ─────────────────────────────────────────────────────────────

    private static void MapConsentEndpoints(RouteGroupBuilder g)
    {
        g.MapPost("/consent", async (
            RecordConsentRequest request,
            ITenantContext tenantContext,
            RecordConsentHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            // Capture raw IP here in the API layer — never pass it deeper
            var rawIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var command = new RecordConsentCommand(
                tenantContext.TenantId,
                tenantContext.UserId,
                request.ConsentType,
                rawIp);

            var result = await handler.HandleAsync(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/identity/consent/{result.Value!.ConsentRecordId}", result.Value)
                : Results.Problem(result.Error!.Message, statusCode: 422);
        })
        .WithName("RecordConsent")
        .Produces<ConsentResponse>(StatusCodes.Status201Created);
    }
}
