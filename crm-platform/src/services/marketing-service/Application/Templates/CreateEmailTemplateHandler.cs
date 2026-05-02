using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CrmPlatform.MarketingService.Domain.Entities;
using CrmPlatform.MarketingService.Domain.Enums;
using CrmPlatform.MarketingService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.MarketingService.Application.Templates;

public sealed record CreateEmailTemplateCommand(
    string        Name,
    string        Subject,
    string        HtmlBody,
    string        PlainTextBody,
    TemplateEngine Engine,
    Guid          RequestedByUserId);

public sealed record CreateEmailTemplateResult(Guid TemplateId, string Name);

public sealed class CreateEmailTemplateHandler(
    MarketingDbContext db,
    ITenantContext tenantContext,
    ILogger<CreateEmailTemplateHandler> logger)
{
    public async Task<Result<CreateEmailTemplateResult>> HandleAsync(
        CreateEmailTemplateCommand command,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return Result.Fail<CreateEmailTemplateResult>("Template name is required.", ResultErrorCode.ValidationError);
        if (string.IsNullOrWhiteSpace(command.Subject))
            return Result.Fail<CreateEmailTemplateResult>("Subject is required.", ResultErrorCode.ValidationError);
        if (string.IsNullOrWhiteSpace(command.HtmlBody))
            return Result.Fail<CreateEmailTemplateResult>("HtmlBody is required.", ResultErrorCode.ValidationError);

        // Prevent duplicate name+version within tenant
        var exists = await db.EmailTemplates
            .AnyAsync(t => t.Name == command.Name.Trim() && t.Version == 1, ct);

        if (exists)
            return Result.Fail<CreateEmailTemplateResult>(
                $"Template '{command.Name}' v1 already exists.", ResultErrorCode.Conflict);

        var template = EmailTemplate.Create(
            tenantContext.TenantId,
            command.Name,
            command.Subject,
            command.HtmlBody,
            command.PlainTextBody,
            command.Engine,
            command.RequestedByUserId);

        db.EmailTemplates.Add(template);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "EmailTemplate {TemplateId} created for tenant {TenantId}",
            template.Id, tenantContext.TenantId);

        return Result.Ok(new CreateEmailTemplateResult(template.Id, template.Name));
    }
}
