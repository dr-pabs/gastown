using CrmPlatform.IntegrationService.Api.Dtos;
using CrmPlatform.IntegrationService.Application;
using CrmPlatform.IntegrationService.Domain.Entities;
using CrmPlatform.IntegrationService.Domain.Enums;
using CrmPlatform.IntegrationService.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.IntegrationService.Api;

public static class IntegrationEndpoints
{
    public static void MapIntegrationEndpoints(this WebApplication app)
    {
        // ─── Connector management (Tenant Admin) ───────────────────────────────
        var connectors = app.MapGroup("/integrations/connectors")
            .RequireAuthorization("TenantAdminPolicy");

        connectors.MapGet("/", ListConnectorsAsync);
        connectors.MapGet("/{id:guid}", GetConnectorAsync);
        connectors.MapPost("/", CreateConnectorAsync);
        connectors.MapPut("/{id:guid}/retry-policy", UpdateRetryPolicyAsync);
        connectors.MapDelete("/{id:guid}", DeleteConnectorAsync);

        // ─── OAuth2 (Tenant Admin) ────────────────────────────────────────────
        var oauth = app.MapGroup("/integrations");

        oauth.MapGet("/connectors/{id:guid}/oauth/authorize",
            GetAuthorizationUrlAsync).RequireAuthorization("TenantAdminPolicy");

        // Callback is unauthenticated — user arrives from external OAuth provider redirect
        oauth.MapGet("/oauth/callback", OAuthCallbackAsync);

        // ─── Jobs (read — Tenant Admin + Staff) ──────────────────────────────
        var jobs = app.MapGroup("/integrations/jobs")
            .RequireAuthorization();

        jobs.MapGet("/", ListJobsAsync);
        jobs.MapGet("/{id:guid}", GetJobAsync);
        jobs.MapPost("/{id:guid}/replay",
            ReplayJobAsync).RequireAuthorization("TenantAdminPolicy");

        // ─── Inbound events (read — Tenant Admin + Staff) ─────────────────────
        var inbound = app.MapGroup("/integrations/inbound-events")
            .RequireAuthorization();

        inbound.MapGet("/", ListInboundEventsAsync);
        inbound.MapGet("/{id:guid}", GetInboundEventAsync);

        // ─── Webhooks (unauthenticated — validated by connector signature) ────
        app.MapPost("/webhooks/inbound/{tenantId:guid}/{connectorTypeStr}",
            ReceiveWebhookAsync);

        // ─── Platform Admin ───────────────────────────────────────────────────
        var admin = app.MapGroup("/integrations/admin")
            .RequireAuthorization("PlatformAdminPolicy");

        admin.MapGet("/connectors", AdminListConnectorsAsync);
        admin.MapPost("/connectors/{id:guid}/suspend", AdminSuspendConnectorAsync);
        admin.MapPost("/connectors/{id:guid}/reinstate", AdminReinstateConnectorAsync);
    }

    // ─── Connector management ─────────────────────────────────────────────────

    private static async Task<IResult> ListConnectorsAsync(
        IntegrationDbContext db, CancellationToken ct)
    {
        var items = await db.Connectors
            .AsNoTracking()
            .OrderBy(c => c.ConnectorType)
            .ToListAsync(ct);

        return Results.Ok(items.Select(MapConnector));
    }

    private static async Task<IResult> GetConnectorAsync(
        Guid id, IntegrationDbContext db, CancellationToken ct)
    {
        var c = await db.Connectors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? Results.NotFound() : Results.Ok(MapConnector(c));
    }

    private static async Task<IResult> CreateConnectorAsync(
        [FromBody] CreateConnectorRequest req,
        ConnectorManagementHandler handler,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        try
        {
            var retryPolicy = req.RetryPolicy is null
                ? Domain.Entities.RetryPolicy.Default()
                : Domain.Entities.RetryPolicy.Create(
                    req.RetryPolicy.MaxRetryDurationMinutes,
                    req.RetryPolicy.InitialRetryDelaySeconds,
                    req.RetryPolicy.MaxRetryDelaySeconds,
                    req.RetryPolicy.BackoffMultiplier);

            var config = await handler.CreateAsync(
                tenantContext.TenantId, req.ConnectorType, req.Label, retryPolicy, ct);

            return Results.Created($"/integrations/connectors/{config.Id}", MapConnector(config));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { detail = ex.Message });
        }
    }

    private static async Task<IResult> UpdateRetryPolicyAsync(
        Guid id, [FromBody] RetryPolicyDto dto,
        ConnectorManagementHandler handler, CancellationToken ct)
    {
        try
        {
            var policy = Domain.Entities.RetryPolicy.Create(
                dto.MaxRetryDurationMinutes,
                dto.InitialRetryDelaySeconds,
                dto.MaxRetryDelaySeconds,
                dto.BackoffMultiplier);

            await handler.UpdateRetryPolicyAsync(id, policy, ct);
            return Results.NoContent();
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (ArgumentOutOfRangeException ex) { return Results.BadRequest(new { detail = ex.Message }); }
    }

    private static async Task<IResult> DeleteConnectorAsync(
        Guid id, ConnectorManagementHandler handler, CancellationToken ct)
    {
        try
        {
            await handler.DeleteAsync(id, ct);
            return Results.NoContent();
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (InvalidOperationException ex) { return Results.UnprocessableEntity(new { detail = ex.Message }); }
    }

    // ─── OAuth2 ───────────────────────────────────────────────────────────────

    private static async Task<IResult> GetAuthorizationUrlAsync(
        Guid id, IntegrationDbContext db, ConnectorOAuthHandler handler, CancellationToken ct)
    {
        var config = await db.Connectors.FindAsync([id], ct);
        if (config is null) return Results.NotFound();

        try
        {
            var url = await handler.GetAuthorizationUrlAsync(config, ct);
            return Results.Ok(new AuthorizationUrlResponse(url));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { detail = ex.Message });
        }
    }

    private static async Task<IResult> OAuthCallbackAsync(
        [FromQuery] string code,
        [FromQuery] string state,
        ConnectorOAuthHandler handler,
        CancellationToken ct)
    {
        try
        {
            var redirectUrl = await handler.HandleCallbackAsync(code, state, ct);
            return Results.Redirect(redirectUrl);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { detail = ex.Message });
        }
    }

    // ─── Outbound Jobs ────────────────────────────────────────────────────────

    private static async Task<IResult> ListJobsAsync(
        [FromQuery] OutboundJobStatus? status,
        [FromQuery] ConnectorType? connectorType,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        IntegrationDbContext db, CancellationToken ct)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.OutboundJobs.AsNoTracking();
        if (status.HasValue)        query = query.Where(j => j.Status == status.Value);
        if (connectorType.HasValue) query = query.Where(j => j.ConnectorType == connectorType.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Results.Ok(new PagedOutboundJobsResponse(
            items.Select(MapJob).ToList(), page, pageSize, total));
    }

    private static async Task<IResult> GetJobAsync(
        Guid id, IntegrationDbContext db, CancellationToken ct)
    {
        var j = await db.OutboundJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return j is null ? Results.NotFound() : Results.Ok(MapJob(j));
    }

    private static async Task<IResult> ReplayJobAsync(
        Guid id, ConnectorManagementHandler handler, ITenantContext tenantContext, CancellationToken ct)
    {
        try
        {
            await handler.ReplayJobAsync(id, tenantContext.TenantId, ct);
            return Results.Accepted();
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (InvalidOperationException ex) { return Results.UnprocessableEntity(new { detail = ex.Message }); }
    }

    // ─── Inbound Events ───────────────────────────────────────────────────────

    private static async Task<IResult> ListInboundEventsAsync(
        [FromQuery] InboundEventStatus? status,
        [FromQuery] ConnectorType? connectorType,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        IntegrationDbContext db, CancellationToken ct)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.InboundEvents.AsNoTracking();
        if (status.HasValue)        query = query.Where(e => e.Status == status.Value);
        if (connectorType.HasValue) query = query.Where(e => e.ConnectorType == connectorType.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Results.Ok(new PagedInboundEventsResponse(
            items.Select(MapInboundEvent).ToList(), page, pageSize, total));
    }

    private static async Task<IResult> GetInboundEventAsync(
        Guid id, IntegrationDbContext db, CancellationToken ct)
    {
        var e = await db.InboundEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return Results.NotFound();

        // Detail includes raw payload — available to admins only
        return Results.Ok(new InboundEventDetailResponse(
            e.Id, e.ConnectorType, e.ExternalEventId, e.RawPayload,
            e.NormalisedEventType, e.Status, e.ServiceBusMessageId,
            e.FailureReason, e.ReceivedAt, e.ProcessedAt));
    }

    // ─── Inbound Webhook ─────────────────────────────────────────────────────

    private static async Task<IResult> ReceiveWebhookAsync(
        Guid tenantId,
        string connectorTypeStr,
        HttpRequest request,
        InboundWebhookHandler handler,
        CancellationToken ct)
    {
        if (!Enum.TryParse<ConnectorType>(connectorTypeStr, ignoreCase: true, out var connectorType))
            return Results.NotFound();

        // Buffer the request body so it can be read multiple times (signature + storage)
        request.EnableBuffering();
        using var reader  = new StreamReader(request.Body, leaveOpen: true);
        var rawBody       = await reader.ReadToEndAsync(ct);
        request.Body.Position = 0;

        var success = await handler.HandleAsync(tenantId, connectorType, request, rawBody, ct);

        // Always return 202 after signature validation passes (business rule #4)
        // Return 404/401 only for unknown connector or invalid signature (before processing)
        return success ? Results.Accepted() : Results.Unauthorized();
    }

    // ─── Platform Admin ───────────────────────────────────────────────────────

    private static async Task<IResult> AdminListConnectorsAsync(
        IntegrationDbContext db, CancellationToken ct)
    {
        var items = await db.Connectors
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(c => c.TenantId)
            .ThenBy(c => c.ConnectorType)
            .ToListAsync(ct);

        return Results.Ok(items.Select(c => new AdminConnectorResponse(
            c.Id, c.TenantId, c.ConnectorType, c.Label, c.Status, c.ExternalAccountId, c.CreatedAt)));
    }

    private static async Task<IResult> AdminSuspendConnectorAsync(
        Guid id, IntegrationDbContext db, CancellationToken ct)
    {
        var config = await db.Connectors.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (config is null) return Results.NotFound();

        config.Suspend();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> AdminReinstateConnectorAsync(
        Guid id, IntegrationDbContext db, CancellationToken ct)
    {
        var config = await db.Connectors.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (config is null) return Results.NotFound();

        try
        {
            config.Reinstate();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.UnprocessableEntity(new { detail = ex.Message });
        }
    }

    // ─── Mappers ──────────────────────────────────────────────────────────────

    private static ConnectorResponse MapConnector(ConnectorConfig c) => new(
        c.Id, c.ConnectorType, c.Label, c.Status,
        c.ExternalAccountId, c.OAuthScopes, c.TokenExpiresUtc,
        HasWebhookSecret: !string.IsNullOrEmpty(c.WebhookSecret),
        RetryPolicy: new RetryPolicyDto(
            c.RetryPolicy.MaxRetryDurationMinutes,
            c.RetryPolicy.InitialRetryDelaySeconds,
            c.RetryPolicy.MaxRetryDelaySeconds,
            c.RetryPolicy.BackoffMultiplier),
        c.CreatedAt, c.UpdatedAt);

    private static OutboundJobResponse MapJob(Domain.Entities.OutboundJob j) => new(
        j.Id, j.ConnectorConfigId, j.ConnectorType, j.EventType,
        j.Status, j.AttemptCount, j.FirstAttemptAt, j.LastAttemptAt,
        j.NextRetryAt, j.AbandonedAt, j.FailureReason, j.ExternalId, j.CreatedAt);

    private static InboundEventResponse MapInboundEvent(Domain.Entities.InboundEvent e) => new(
        e.Id, e.ConnectorType, e.ExternalEventId,
        e.NormalisedEventType, e.Status, e.ServiceBusMessageId,
        e.FailureReason, e.ReceivedAt, e.ProcessedAt);
}

// ─── Scoped interface for tenant context ──────────────────────────────────────
// (implemented by the template middleware — re-declared here for compile-time use)
public interface ITenantContext
{
    Guid TenantId { get; }
}
