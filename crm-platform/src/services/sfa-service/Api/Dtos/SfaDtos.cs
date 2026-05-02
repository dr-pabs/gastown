using CrmPlatform.SfaService.Domain.Enums;

namespace CrmPlatform.SfaService.Api.Dtos;

// ─── Lead DTOs ────────────────────────────────────────────────────────────────

public sealed record CreateLeadRequest(
    string     Name,
    string     Email,
    string?    Phone,
    string?    Company,
    LeadSource Source);

public sealed record UpdateLeadRequest(
    string  Name,
    string  Email,
    string? Phone,
    string? Company);

public sealed record AssignLeadRequest(Guid AssignedToUserId);

public sealed record ConvertLeadRequest(
    string   OpportunityTitle,
    decimal  OpportunityValue,
    Guid?    ContactId,
    Guid?    AccountId,
    Guid?    AssignedToUserId);

public sealed record LeadResponse(
    Guid        Id,
    string      Name,
    string      Email,
    string?     Phone,
    string?     Company,
    string      Source,
    string      Status,
    int         Score,
    Guid?       AssignedToUserId,
    bool        IsConverted,
    Guid?       ConvertedToOpportunityId,
    DateTime    CreatedAt,
    DateTime    UpdatedAt);

public sealed record PagedLeadsResponse(
    IReadOnlyList<LeadResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

// ─── Opportunity DTOs ─────────────────────────────────────────────────────────

public sealed record CreateOpportunityRequest(
    string          Title,
    decimal         Value,
    Guid?           ContactId,
    Guid?           AccountId,
    Guid?           AssignedToUserId,
    DateTime?       CloseDate);

public sealed record AdvanceStageRequest(OpportunityStage NewStage);

public sealed record OpportunityResponse(
    Guid             Id,
    string           Title,
    string           Stage,
    decimal          Value,
    DateTime?        CloseDate,
    Guid?            ContactId,
    Guid?            AccountId,
    Guid?            AssignedToUserId,
    Guid?            ConvertedFromLeadId,
    DateTime         CreatedAt,
    DateTime         UpdatedAt);

public sealed record PagedOpportunitiesResponse(
    IReadOnlyList<OpportunityResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

// ─── Contact DTOs ─────────────────────────────────────────────────────────────

public sealed record CreateContactRequest(
    string  FirstName,
    string  LastName,
    string  Email,
    string? Phone,
    Guid?   AccountId);

public sealed record ContactResponse(
    Guid     Id,
    string   FirstName,
    string   LastName,
    string   Email,
    string?  Phone,
    Guid?    AccountId,
    DateTime CreatedAt);

// ─── Account DTOs ─────────────────────────────────────────────────────────────

public sealed record CreateAccountRequest(
    string      Name,
    string?     Industry,
    AccountSize Size,
    string?     BillingAddress,
    string?     Website);

public sealed record AccountResponse(
    Guid        Id,
    string      Name,
    string?     Industry,
    string      Size,
    string?     BillingAddress,
    string?     Website,
    DateTime    CreatedAt);

// ─── Activity DTOs ────────────────────────────────────────────────────────────

public sealed record CreateActivityRequest(
    ActivityType ActivityType,
    Guid         RelatedEntityId,
    string       RelatedEntityType,   // "Lead" | "Opportunity" | "Contact"
    DateTime     OccurredAt,
    string?      Notes);

public sealed record ActivityResponse(
    Guid         Id,
    string       ActivityType,
    Guid         RelatedEntityId,
    string       RelatedEntityType,
    DateTime     OccurredAt,
    Guid         AuthorUserId,
    string?      Notes,
    DateTime     CreatedAt);

// ─── Shared ───────────────────────────────────────────────────────────────────

public sealed record ConvertLeadResponse(Guid OpportunityId);
public sealed record CreatedResponse(Guid Id);
