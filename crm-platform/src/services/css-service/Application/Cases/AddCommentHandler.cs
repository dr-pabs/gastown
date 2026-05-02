using CrmPlatform.CssService.Domain.Entities;
using CrmPlatform.CssService.Domain.Enums;
using CrmPlatform.CssService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.CssService.Application.Cases;

public sealed record AddCommentCommand(
    Guid             CaseId,
    string           Body,
    bool             IsInternal,
    CommentAuthorType AuthorType);

public sealed class AddCommentHandler(
    CssDbContext db,
    ITenantContext tenantContext)
{
    public async Task<Result<Guid>> HandleAsync(
        AddCommentCommand cmd, CancellationToken ct = default)
    {
        var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == cmd.CaseId, ct);

        if (c is null)
            return Result.Fail<Guid>("Case not found.", ResultErrorCode.NotFound);

        if (c.Status == CaseStatus.Closed)
            return Result.Fail<Guid>("Case is closed and immutable.", ResultErrorCode.ValidationError);

        CaseComment comment;
        try
        {
            comment = CaseComment.Create(
                tenantContext.TenantId,
                cmd.CaseId,
                tenantContext.UserId,
                cmd.AuthorType,
                cmd.Body,
                cmd.IsInternal);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail<Guid>(ex.Message, ResultErrorCode.ValidationError);
        }

        // If customer replies on a Pending case, resume it
        if (cmd.AuthorType == CommentAuthorType.Customer && c.Status == CaseStatus.Pending)
            c.Resume();

        db.CaseComments.Add(comment);
        await db.SaveChangesAsync(ct);
        return Result.Ok(comment.Id);
    }
}
