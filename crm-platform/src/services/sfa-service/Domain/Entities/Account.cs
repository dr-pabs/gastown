using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.SfaService.Domain.Entities;

public sealed class Account : BaseEntity
{
    private Account() { } // EF Core

    public string  Name           { get; private set; } = string.Empty;
    public string? Industry       { get; private set; }
    public string? Size           { get; private set; }
    public string? BillingAddress { get; private set; }
    public string? Website        { get; private set; }

    // Navigation
    public IReadOnlyList<Contact>     Contacts      { get; private set; } = [];
    public IReadOnlyList<Opportunity> Opportunities { get; private set; } = [];

    public static Account Create(
        Guid tenantId,
        string name,
        string? industry = null,
        string? size = null,
        string? billingAddress = null,
        string? website = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Account
        {
            TenantId       = tenantId,
            Name           = name,
            Industry       = industry,
            Size           = size,
            BillingAddress = billingAddress,
            Website        = website,
        };
    }

    public void Update(string? name, string? industry, string? size, string? billingAddress, string? website)
    {
        if (!string.IsNullOrWhiteSpace(name))     Name           = name;
        if (industry       != null) Industry       = industry;
        if (size           != null) Size           = size;
        if (billingAddress != null) BillingAddress = billingAddress;
        if (website        != null) Website        = website;
    }
}
