using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.SfaService.Domain.Entities;

public sealed class Contact : BaseEntity
{
    private Contact() { } // EF Core

    public string  FirstName { get; private set; } = string.Empty;
    public string  LastName  { get; private set; } = string.Empty;
    public string  Email     { get; private set; } = string.Empty;
    public string? Phone     { get; private set; }
    public Guid?   AccountId { get; private set; }

    // Navigation
    public Account?               Account       { get; private set; }
    public IReadOnlyList<Opportunity> Opportunities { get; private set; } = [];

    public static Contact Create(
        Guid tenantId,
        string firstName,
        string lastName,
        string email,
        string? phone = null,
        Guid? accountId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        return new Contact
        {
            TenantId  = tenantId,
            FirstName = firstName,
            LastName  = lastName,
            Email     = email.ToLowerInvariant(),
            Phone     = phone,
            AccountId = accountId,
        };
    }

    public void Update(string? firstName, string? lastName, string? email, string? phone)
    {
        if (!string.IsNullOrWhiteSpace(firstName)) FirstName = firstName;
        if (!string.IsNullOrWhiteSpace(lastName))  LastName  = lastName;
        if (!string.IsNullOrWhiteSpace(email))     Email     = email.ToLowerInvariant();
        if (phone != null) Phone = phone;
    }
}
