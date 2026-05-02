using CrmPlatform.SfaService.Api.Dtos;
using CrmPlatform.SfaService.Application.Accounts;
using CrmPlatform.SfaService.Application.Contacts;
using CrmPlatform.SfaService.Application.Leads;
using CrmPlatform.SfaService.Application.Opportunities;
using CrmPlatform.SfaService.Domain.Entities;
using CrmPlatform.SfaService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.SfaService.Api;

public static class SfaEndpoints
{
    public static IEndpointRouteBuilder MapSfaEndpoints(this IEndpointRouteBuilder app)
    {
        var leads  = app.MapGroup("/leads").RequireAuthorization();
        var opps   = app.MapGroup("/opportunities").RequireAuthorization();
        var contts = app.MapGroup("/contacts").RequireAuthorization();
        var accts  = app.MapGroup("/accounts").RequireAuthorization();
        var acts   = app.MapGroup("/activities").RequireAuthorization();

        // ─── Leads ────────────────────────────────────────────────────────────

        leads.MapGet("/", async (
            SfaDbContext db,
            int page     = 1,
            int pageSize = 25) =>
        {
            pageSize = Math.Min(pageSize, 100);
            var query = db.Leads.OrderByDescending(l => l.Score).ThenBy(l => l.CreatedAt);
            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Results.Ok(new PagedLeadsResponse(items.Select(ToLeadResponse).ToList(), total, page, pageSize));
        });

        leads.MapGet("/{id:guid}", async (Guid id, SfaDbContext db) =>
        {
            var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id);
            return lead is null ? Results.NotFound() : Results.Ok(ToLeadResponse(lead));
        });

        leads.MapPost("/", async (
            CreateLeadRequest req,
            CreateLeadHandler handler,
            ITenantContext ctx) =>
        {
            var result = await handler.HandleAsync(new CreateLeadCommand(
                req.Name, req.Email, req.Phone, req.Company, req.Source, ctx.UserId));

            return result.IsSuccess
                ? Results.Created($"/leads/{result.Value}", new CreatedResponse(result.Value))
                : result.ToHttpResult();
        });

        leads.MapPut("/{id:guid}", async (
            Guid id,
            UpdateLeadRequest req,
            UpdateLeadHandler handler) =>
        {
            var result = await handler.HandleAsync(
                new UpdateLeadCommand(id, req.Name, req.Email, req.Phone, req.Company));
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        });

        leads.MapPost("/{id:guid}/assign", async (
            Guid id,
            AssignLeadRequest req,
            AssignLeadHandler handler,
            ITenantContext ctx) =>
        {
            var result = await handler.HandleAsync(
                new AssignLeadCommand(req.AssignedToUserId, ctx.UserId, id));
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        });

        leads.MapPost("/{id:guid}/convert", async (
            Guid id,
            ConvertLeadRequest req,
            ConvertLeadHandler handler) =>
        {
            var result = await handler.HandleAsync(new ConvertLeadCommand(
                id,
                req.OpportunityTitle,
                req.OpportunityValue,
                req.ContactId,
                req.AccountId,
                req.AssignedToUserId));

            return result.IsSuccess
                ? Results.Ok(new ConvertLeadResponse(result.Value!.OpportunityId))
                : result.ToHttpResult();
        });

        leads.MapDelete("/{id:guid}", async (Guid id, DeleteLeadHandler handler) =>
        {
            var result = await handler.HandleAsync(id);
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        });

        // ─── Opportunities ────────────────────────────────────────────────────

        opps.MapGet("/", async (
            SfaDbContext db,
            int page     = 1,
            int pageSize = 25) =>
        {
            pageSize = Math.Min(pageSize, 100);
            var query = db.Opportunities.OrderByDescending(o => o.Value).ThenBy(o => o.CreatedAt);
            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Results.Ok(new PagedOpportunitiesResponse(items.Select(ToOppResponse).ToList(), total, page, pageSize));
        });

        opps.MapGet("/{id:guid}", async (Guid id, SfaDbContext db) =>
        {
            var opp = await db.Opportunities.FirstOrDefaultAsync(o => o.Id == id);
            return opp is null ? Results.NotFound() : Results.Ok(ToOppResponse(opp));
        });

        opps.MapPost("/", async (
            CreateOpportunityRequest req,
            CreateOpportunityHandler handler) =>
        {
            var result = await handler.HandleAsync(new CreateOpportunityCommand(
                req.Title, req.Value, req.ContactId, req.AccountId, req.AssignedToUserId, req.CloseDate));
            return result.IsSuccess
                ? Results.Created($"/opportunities/{result.Value}", new CreatedResponse(result.Value))
                : result.ToHttpResult();
        });

        opps.MapPost("/{id:guid}/stage", async (
            Guid id,
            AdvanceStageRequest req,
            AdvanceStageHandler handler) =>
        {
            var result = await handler.HandleAsync(new AdvanceStageCommand(id, req.NewStage));
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        });

        // ─── Contacts ─────────────────────────────────────────────────────────

        contts.MapPost("/", async (
            CreateContactRequest req,
            CreateContactHandler handler) =>
        {
            var result = await handler.HandleAsync(new CreateContactCommand(
                req.FirstName, req.LastName, req.Email, req.Phone, req.AccountId));
            return result.IsSuccess
                ? Results.Created($"/contacts/{result.Value}", new CreatedResponse(result.Value))
                : result.ToHttpResult();
        });

        contts.MapGet("/{id:guid}", async (Guid id, SfaDbContext db) =>
        {
            var c = await db.Contacts.FirstOrDefaultAsync(x => x.Id == id);
            return c is null ? Results.NotFound() : Results.Ok(ToContactResponse(c));
        });

        // ─── Accounts ─────────────────────────────────────────────────────────

        accts.MapPost("/", async (
            CreateAccountRequest req,
            CreateAccountHandler handler) =>
        {
            var result = await handler.HandleAsync(new CreateAccountCommand(
                req.Name, req.Industry, req.Size, req.BillingAddress, req.Website));
            return result.IsSuccess
                ? Results.Created($"/accounts/{result.Value}", new CreatedResponse(result.Value))
                : result.ToHttpResult();
        });

        accts.MapGet("/{id:guid}", async (Guid id, SfaDbContext db) =>
        {
            var a = await db.Accounts.FirstOrDefaultAsync(x => x.Id == id);
            return a is null ? Results.NotFound() : Results.Ok(ToAccountResponse(a));
        });

        // ─── Activities ───────────────────────────────────────────────────────

        acts.MapPost("/", async (
            CreateActivityRequest req,
            SfaDbContext db,
            ITenantContext ctx) =>
        {
            var activity = Activity.Record(
                ctx.TenantId,
                req.ActivityType,
                req.RelatedEntityId,
                req.RelatedEntityType,
                req.OccurredAt,
                ctx.UserId,
                req.Notes);

            db.Activities.Add(activity);
            await db.SaveChangesAsync();
            return Results.Created($"/activities/{activity.Id}", new CreatedResponse(activity.Id));
        });

        acts.MapGet("/", async (
            Guid relatedEntityId,
            SfaDbContext db) =>
        {
            var items = await db.Activities
                .Where(a => a.RelatedEntityId == relatedEntityId)
                .OrderByDescending(a => a.OccurredAt)
                .ToListAsync();

            return Results.Ok(items.Select(ToActivityResponse));
        });

        return app;
    }

    // ─── Projection helpers ───────────────────────────────────────────────────

    private static LeadResponse ToLeadResponse(Lead l) => new(
        l.Id, l.Name, l.Email, l.Phone, l.Company,
        l.Source.ToString(), l.Status.ToString(), l.Score,
        l.AssignedToUserId, l.IsConverted, l.ConvertedToOpportunityId,
        l.CreatedAt, l.UpdatedAt);

    private static OpportunityResponse ToOppResponse(Opportunity o) => new(
        o.Id, o.Title, o.Stage.ToString(), o.Value, o.CloseDate,
        o.ContactId, o.AccountId, o.AssignedToUserId, o.ConvertedFromLeadId,
        o.CreatedAt, o.UpdatedAt);

    private static ContactResponse ToContactResponse(Contact c) => new(
        c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.AccountId, c.CreatedAt);

    private static AccountResponse ToAccountResponse(Account a) => new(
        a.Id, a.Name, a.Industry, a.Size ?? string.Empty, a.BillingAddress, a.Website, a.CreatedAt);

    private static ActivityResponse ToActivityResponse(Activity a) => new(
        a.Id, a.ActivityType.ToString(), a.RelatedEntityId, a.RelatedEntityType,
        a.OccurredAt, a.AuthorUserId, a.Notes, a.CreatedAt);
}
