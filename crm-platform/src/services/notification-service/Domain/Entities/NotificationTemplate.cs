using CrmPlatform.NotificationService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Domain;
using HandlebarsDotNet;

namespace CrmPlatform.NotificationService.Domain.Entities;

/// <summary>
/// Staff-managed notification template. Immutable once IsActive = true.
/// Create a new version (Version++) to update an active template.
/// </summary>
public sealed class NotificationTemplate : BaseEntity
{
    public string               Name              { get; private set; } = string.Empty;
    public NotificationCategory Category          { get; private set; }
    public NotificationChannel  Channel           { get; private set; }
    public string?              SubjectTemplate   { get; private set; }   // email / push only
    public string?              BodyHtmlTemplate  { get; private set; }   // email / web push
    public string               BodyPlainTemplate { get; private set; } = string.Empty;
    public bool                 IsActive          { get; private set; }
    public int                  Version           { get; private set; } = 1;

    private NotificationTemplate() { }

    public static NotificationTemplate Create(
        Guid                tenantId,
        string              name,
        NotificationCategory category,
        NotificationChannel  channel,
        string               bodyPlain,
        string?              subjectTemplate  = null,
        string?              bodyHtmlTemplate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPlain);

        return new NotificationTemplate
        {
            Id               = Guid.NewGuid(),
            TenantId         = tenantId,
            Name             = name.Trim(),
            Category         = category,
            Channel          = channel,
            SubjectTemplate  = subjectTemplate?.Trim(),
            BodyHtmlTemplate = bodyHtmlTemplate?.Trim(),
            BodyPlainTemplate = bodyPlain.Trim(),
            IsActive         = false,
            Version          = 1
        };
    }

    /// <summary>Updates content. Only allowed while IsActive = false.</summary>
    public void UpdateContent(
        string  bodyPlain,
        string? subjectTemplate  = null,
        string? bodyHtmlTemplate = null)
    {
        if (IsActive)
            throw new InvalidOperationException(
                "Cannot update an active template. Deactivate or create a new version.");

        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPlain);

        SubjectTemplate   = subjectTemplate?.Trim();
        BodyHtmlTemplate  = bodyHtmlTemplate?.Trim();
        BodyPlainTemplate = bodyPlain.Trim();
        Version++;
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>Renders subject + body using Handlebars substitution.</summary>
    public RenderedNotification Render(IDictionary<string, object?> variables)
    {
        var subject  = SubjectTemplate  is not null ? Compile(SubjectTemplate,  variables) : null;
        var bodyHtml = BodyHtmlTemplate is not null ? Compile(BodyHtmlTemplate, variables) : null;
        var bodyPlain = Compile(BodyPlainTemplate, variables);

        return new RenderedNotification(subject, bodyHtml, bodyPlain);
    }

    private static string Compile(string template, IDictionary<string, object?> data)
    {
        var compiled = Handlebars.Compile(template);
        return compiled(data);
    }
}

public sealed record RenderedNotification(
    string? Subject,
    string? BodyHtml,
    string  BodyPlain);
