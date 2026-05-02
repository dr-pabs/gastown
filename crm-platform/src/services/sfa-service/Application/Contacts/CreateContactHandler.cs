using CrmPlatform.SfaService.Domain.Entities;
using CrmPlatform.SfaService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.SfaService.Application.Contacts;

public sealed record CreateContactCommand(
    string  FirstName,
    string  LastName,
    string  Email,
    string? Phone,
    Guid?   AccountId);

public sealed class CreateContactHandler(
    SfaDbContext db,
    ITenantContext tenantContext)
{
    public async Task<Result<Guid>> HandleAsync(
        CreateContactCommand cmd, CancellationToken ct = default)
    {
        var existing = await db.Contacts
            .AnyAsync(c => c.Email == cmd.Email.ToLowerInvariant(), ct);

        if (existing)
            return Result.Fail<Guid>(
                $"A contact with email {cmd.Email} already exists.", ResultErrorCode.Conflict);

        var contact = Contact.Create(
            tenantContext.TenantId,
            cmd.FirstName,
            cmd.LastName,
            cmd.Email,
            cmd.Phone,
            cmd.AccountId);

        db.Contacts.Add(contact);
        await db.SaveChangesAsync(ct);
        return Result.Ok(contact.Id);
    }
}
