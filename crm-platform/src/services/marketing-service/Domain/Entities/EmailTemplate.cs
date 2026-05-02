using CrmPlatform.MarketingService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.MarketingService.Domain.Entities;

/// <summary>
/// Reusable HTML email template with variable substitution support.
/// Templates are versioned — once a version is published it is immutable.
/// </summary>
public sealed class EmailTemplate : BaseEntity
{
    public string         Name          { get; private set; } = string.Empty;
    public string         Subject       { get; private set; } = string.Empty;
    public string         HtmlBody      { get; private set; } = string.Empty;
    public string         PlainTextBody { get; private set; } = string.Empty;
    public TemplateEngine Engine        { get; private set; }
    public int            Version       { get; private set; }
    public bool           IsPublished   { get; private set; }
    public Guid           CreatedByUserId { get; private set; }

    private EmailTemplate() { } // EF Core

    public static EmailTemplate Create(
        Guid          tenantId,
        string        name,
        string        subject,
        string        htmlBody,
        string        plainTextBody,
        TemplateEngine engine,
        Guid          createdByUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(htmlBody);

        return new EmailTemplate
        {
            Id              = Guid.NewGuid(),
            TenantId        = tenantId,
            Name            = name.Trim(),
            Subject         = subject.Trim(),
            HtmlBody        = htmlBody,
            PlainTextBody   = plainTextBody,
            Engine          = engine,
            Version         = 1,
            IsPublished     = false,
            CreatedByUserId = createdByUserId,
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow
        };
    }

    /// <summary>Update draft content. Immutable once published.</summary>
    public void UpdateContent(string subject, string htmlBody, string plainTextBody)
    {
        if (IsPublished)
            throw new InvalidOperationException("Published templates are immutable. Create a new version.");

        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(htmlBody);

        Subject       = subject.Trim();
        HtmlBody      = htmlBody;
        PlainTextBody = plainTextBody;
        UpdatedAt     = DateTime.UtcNow;
    }

    /// <summary>Publish the template — makes it available for use in journeys.</summary>
    public void Publish()
    {
        if (IsPublished)
            throw new InvalidOperationException("Template is already published.");

        IsPublished = true;
        UpdatedAt   = DateTime.UtcNow;
    }
}
