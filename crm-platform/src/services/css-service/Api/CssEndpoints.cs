using CrmPlatform.CssService.Api.Dtos;
using CrmPlatform.CssService.Application.Cases;
using CrmPlatform.CssService.Application.Sla;
using CrmPlatform.CssService.Domain.Entities;
using CrmPlatform.CssService.Domain.Enums;
using CrmPlatform.CssService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CrmPlatform.CssService.Api;

public static class CssEndpoints
{
    public static IEndpointRouteBuilder MapCssEndpoints(this IEndpointRouteBuilder app)
    {
        var cases         = app.MapGroup("/cases").RequireAuthorization();
        var customerCases = app.MapGroup("/customer/cases").RequireAuthorization();
        var policies      = app.MapGroup("/sla-policies").RequireAuthorization();

        // ─── Cases ────────────────────────────────────────────────────────────

        cases.MapGet("/", async (
            CssDbContext db,
            ITenantContext ctx,
            string? status   = null,
            string? priority = null,
            Guid?   assignee = null,
            int     page     = 1,
            int     pageSize = 25) =>
        {
            pageSize = Math.Min(pageSize, 100);

            var query = db.Cases.AsQueryable();

            // Customer portal: restrict to own AccountId via companyId claim
            if (ctx.Role == "CustomerPortal" && ctx is ICompanyContext companyCtx)
                query = query.Where(c => c.AccountId == companyCtx.CompanyId);

            if (Enum.TryParse<CaseStatus>(status, ignoreCase: true, out var parsedStatus))
                query = query.Where(c => c.Status == parsedStatus);

            if (Enum.TryParse<CasePriority>(priority, ignoreCase: true, out var parsedPriority))
                query = query.Where(c => c.Priority == parsedPriority);

            if (assignee.HasValue)
                query = query.Where(c => c.AssignedToUserId == assignee);

            query = query.OrderByDescending(c => c.Priority).ThenBy(c => c.CreatedAt);

            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Results.Ok(new PagedCasesResponse(
                items.Select(ToCaseResponse).ToList(), total, page, pageSize));
        });

        cases.MapGet("/{id:guid}", async (Guid id, CssDbContext db, ITenantContext ctx) =>
        {
            var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();

            // Company isolation check
            if (ctx.Role == "CustomerPortal" && ctx is ICompanyContext companyCtx
                && c.AccountId != companyCtx.CompanyId)
                return Results.Forbid();

            return Results.Ok(ToCaseResponse(c));
        });

        cases.MapPost("/", async (
            CreateCaseRequest req,
            CreateCaseHandler handler) =>
        {
            var result = await handler.HandleAsync(new CreateCaseCommand(
                req.Title, req.Description, req.Priority,
                req.Channel, req.ContactId, req.AccountId));

            return result.IsSuccess
                ? Results.Created($"/cases/{result.Value}", new CreatedResponse(result.Value))
                : result.ToHttpResult();
        });

        cases.MapPatch("/{id:guid}/status", async (
            Guid id,
            TransitionStatusRequest req,
            TransitionStatusHandler handler) =>
        {
            var result = await handler.HandleAsync(new TransitionStatusCommand(id, req.NewStatus));
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        });

        cases.MapPost("/{id:guid}/assign", async (
            Guid id,
            AssignCaseRequest req,
            AssignCaseHandler handler) =>
        {
            var result = await handler.HandleAsync(new AssignCaseCommand(id, req.AssignedToUserId));
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        });

        cases.MapPost("/{id:guid}/escalate", async (
            Guid id,
            EscalateCaseRequest req,
            EscalateCaseHandler handler) =>
        {
            var result = await handler.HandleAsync(
                new EscalateCaseCommand(id, req.Reason, req.NewAssigneeId));
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        });

        cases.MapPost("/{id:guid}/comments", async (
            Guid id,
            AddCommentRequest req,
            AddCommentHandler handler) =>
        {
            var result = await handler.HandleAsync(
                new AddCommentCommand(id, req.Body, req.IsInternal, req.AuthorType));
            return result.IsSuccess
                ? Results.Created($"/cases/{id}/comments/{result.Value}", new CreatedResponse(result.Value))
                : result.ToHttpResult();
        });

        cases.MapGet("/{id:guid}/comments", async (
            Guid id,
            CssDbContext db,
            ITenantContext ctx) =>
        {
            var isStaff = ctx.Role is not "CustomerPortal";

            var comments = await db.CaseComments
                .Where(c => c.CaseId == id)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync();

            // Filter internal comments for non-staff callers
            var filtered = isStaff
                ? comments
                : comments.Where(c => !c.IsInternal).ToList();

            return Results.Ok(filtered.Select(ToCommentResponse));
        });

        // ─── Customer headless cases ────────────────────────────────────────────

        customerCases.MapGet("/my", async (
            CssDbContext db,
            ITenantContext ctx,
            ClaimsPrincipal user,
            string? status = null,
            string? priority = null,
            int page = 1,
            int pageSize = 25) =>
        {
            pageSize = Math.Min(pageSize, 100);
            var companyId = TryGetCompanyId(user);

            var query = db.Cases
                .Where(c => IsCustomerAccessible(c, ctx.UserId, companyId));

            if (Enum.TryParse<CaseStatus>(status, ignoreCase: true, out var parsedStatus))
                query = query.Where(c => c.Status == parsedStatus);

            if (Enum.TryParse<CasePriority>(priority, ignoreCase: true, out var parsedPriority))
                query = query.Where(c => c.Priority == parsedPriority);

            query = query.OrderByDescending(c => c.Priority).ThenByDescending(c => c.CreatedAt);

            var totalCount = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Results.Ok(new CustomerPagedCasesResponse(
                items.Select(ToCustomerCaseResponse).ToList(),
                totalCount,
                page,
                pageSize,
                totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
        });

        customerCases.MapGet("/{id:guid}", async (
            Guid id,
            CssDbContext db,
            ITenantContext ctx,
            ClaimsPrincipal user) =>
        {
            var companyId = TryGetCompanyId(user);
            var supportCase = await db.Cases.FirstOrDefaultAsync(x => x.Id == id);

            if (supportCase is null)
                return Results.NotFound();

            return IsCustomerAccessible(supportCase, ctx.UserId, companyId)
                ? Results.Ok(ToCustomerCaseResponse(supportCase))
                : Results.Forbid();
        });

        customerCases.MapPost("/", async (
            CustomerCreateCaseRequest req,
            CssDbContext db,
            ITenantContext ctx,
            ClaimsPrincipal user,
            CreateCaseHandler handler) =>
        {
            var result = await handler.HandleAsync(new CreateCaseCommand(
                req.Subject,
                req.Description ?? req.Subject,
                req.Priority,
                CaseChannel.Portal,
                ctx.UserId,
                TryGetCompanyId(user)));

            if (!result.IsSuccess)
                return result.ToHttpResult();

            var supportCase = await db.Cases.FirstAsync(x => x.Id == result.Value);

            return Results.Created($"/customer/cases/{result.Value}", ToCustomerCaseResponse(supportCase));
        });

        customerCases.MapGet("/{id:guid}/comments", async (
            Guid id,
            CssDbContext db,
            ITenantContext ctx,
            ClaimsPrincipal user) =>
        {
            var companyId = TryGetCompanyId(user);
            var supportCase = await db.Cases.FirstOrDefaultAsync(x => x.Id == id);

            if (supportCase is null)
                return Results.NotFound();

            if (!IsCustomerAccessible(supportCase, ctx.UserId, companyId))
                return Results.Forbid();

            var comments = await db.CaseComments
                .Where(c => c.CaseId == id && !c.IsInternal)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync();

            return Results.Ok(comments.Select(ToCustomerCommentResponse));
        });

        customerCases.MapPost("/{id:guid}/comments", async (
            Guid id,
            CustomerAddCommentRequest req,
            CssDbContext db,
            ITenantContext ctx,
            ClaimsPrincipal user,
            AddCommentHandler handler) =>
        {
            var companyId = TryGetCompanyId(user);
            var supportCase = await db.Cases.FirstOrDefaultAsync(x => x.Id == id);

            if (supportCase is null)
                return Results.NotFound();

            if (!IsCustomerAccessible(supportCase, ctx.UserId, companyId))
                return Results.Forbid();

            var result = await handler.HandleAsync(
                new AddCommentCommand(id, req.Body, isInternal: false, CommentAuthorType.Customer));

            if (!result.IsSuccess)
                return result.ToHttpResult();

            var comment = await db.CaseComments.FirstAsync(x => x.Id == result.Value);
            return Results.Created($"/customer/cases/{id}/comments/{result.Value}", ToCustomerCommentResponse(comment));
        });

        customerCases.MapPost("/{id:guid}/close", async (
            Guid id,
            CssDbContext db,
            ITenantContext ctx,
            ClaimsPrincipal user,
            CloseCaseHandler handler) =>
        {
            var companyId = TryGetCompanyId(user);
            var supportCase = await db.Cases.FirstOrDefaultAsync(x => x.Id == id);

            if (supportCase is null)
                return Results.NotFound();

            if (!IsCustomerAccessible(supportCase, ctx.UserId, companyId))
                return Results.Forbid();

            var result = await handler.HandleAsync(new CloseCaseCommand(id));
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        });

        // ─── SLA Policies ─────────────────────────────────────────────────────

        policies.MapGet("/", async (CssDbContext db) =>
        {
            var items = await db.SlaPolicies
                .OrderBy(p => p.Priority)
                .ToListAsync();
            return Results.Ok(items.Select(ToSlaPolicyResponse));
        });

        policies.MapPost("/", async (
            CreateSlaPolicyRequest req,
            CreateSlaPolicyHandler handler) =>
        {
            var result = await handler.HandleAsync(new CreateSlaPolicyCommand(
                req.Name, req.Priority, req.FirstResponseMinutes,
                req.ResolutionMinutes, req.BusinessHoursOnly));

            return result.IsSuccess
                ? Results.Created($"/sla-policies/{result.Value}", new CreatedResponse(result.Value))
                : result.ToHttpResult();
        });

        return app;
    }

    // ─── Projection helpers ───────────────────────────────────────────────────

    private static CaseResponse ToCaseResponse(Case c) => new(
        c.Id, c.Title, c.Description,
        c.Status.ToString(), c.Priority.ToString(), c.Channel.ToString(),
        c.ContactId, c.AccountId, c.AssignedToUserId,
        c.SlaDeadline, c.SlaBreached,
        c.CreatedAt, c.UpdatedAt);

    private static CommentResponse ToCommentResponse(CaseComment c) => new(
        c.Id, c.CaseId, c.AuthorId,
        c.AuthorType.ToString(), c.Body, c.IsInternal, c.CreatedAt);

    private static CustomerCaseResponse ToCustomerCaseResponse(Case c) => new(
        c.Id,
        c.TenantId,
        c.ContactId,
        c.AccountId,
        c.AssignedToUserId,
        c.Title,
        c.Description,
        ToCustomerCaseStatus(c.Status),
        c.Priority.ToString(),
        null,
        c.SlaBreached,
        c.SlaDeadline,
        null,
        c.Status is CaseStatus.Resolved or CaseStatus.Closed ? c.UpdatedAt : null,
        false,
        c.CreatedAt,
        c.UpdatedAt);

    private static CustomerCommentResponse ToCustomerCommentResponse(CaseComment c) => new(
        c.Id,
        c.CaseId,
        c.AuthorId,
        null,
        c.Body,
        c.IsInternal,
        c.CreatedAt);

    private static SlaPolicyResponse ToSlaPolicyResponse(SlaPolicy p) => new(
        p.Id, p.Name, p.Priority.ToString(),
        p.FirstResponseMinutes, p.ResolutionMinutes, p.BusinessHoursOnly, p.CreatedAt);

    private static Guid? TryGetCompanyId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue("companyId");
        return Guid.TryParse(raw, out var companyId) && companyId != Guid.Empty
            ? companyId
            : null;
    }

    private static bool IsCustomerAccessible(Case supportCase, Guid userId, Guid? companyId) =>
        supportCase.ContactId == userId
        || (companyId.HasValue && supportCase.AccountId == companyId.Value);

    private static string ToCustomerCaseStatus(CaseStatus status) => status switch
    {
        CaseStatus.New => "Open",
        CaseStatus.Open => "InProgress",
        CaseStatus.Pending => "WaitingOnCustomer",
        CaseStatus.Escalated => "InProgress",
        CaseStatus.Resolved => "Resolved",
        CaseStatus.Closed => "Closed",
        _ => status.ToString()
    };
}

/// <summary>
/// Optional second isolation context for customer portal users.
/// Resolved from ITenantContext when the caller role is CustomerPortal.
/// </summary>
public interface ICompanyContext
{
    Guid CompanyId { get; }
}
