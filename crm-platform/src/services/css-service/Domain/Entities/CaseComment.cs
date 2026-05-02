using CrmPlatform.CssService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.CssService.Domain.Entities;

public sealed class CaseComment : BaseEntity
{
    public Guid              CaseId     { get; private set; }
    public Guid              AuthorId   { get; private set; }
    public CommentAuthorType AuthorType { get; private set; }
    public string            Body       { get; private set; } = string.Empty;
    public bool              IsInternal { get; private set; }

    private CaseComment() { }

    public static CaseComment Create(
        Guid             tenantId,
        Guid             caseId,
        Guid             authorId,
        CommentAuthorType authorType,
        string           body,
        bool             isInternal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        // Customer portal users cannot post internal comments
        if (authorType == CommentAuthorType.Customer && isInternal)
            throw new InvalidOperationException("Customers cannot post internal comments.");

        return new CaseComment
        {
            Id         = Guid.NewGuid(),
            TenantId   = tenantId,
            CaseId     = caseId,
            AuthorId   = authorId,
            AuthorType = authorType,
            Body       = body.Trim(),
            IsInternal = isInternal,
            CreatedAt  = DateTime.UtcNow,
            UpdatedAt  = DateTime.UtcNow
        };
    }
}
