using Microsoft.Extensions.Logging;
using CrmPlatform.IdentityService.Domain.Entities;
using CrmPlatform.IdentityService.Domain.Enums;
using CrmPlatform.IdentityService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.IdentityService.Application.Consent;

public sealed record RecordConsentCommand(
    Guid       TenantId,
    Guid       UserId,
    ConsentType ConsentType,
    string     RawIpAddress);

public sealed record RecordConsentResult(Guid ConsentRecordId);

public sealed class RecordConsentHandler(
    IdentityDbContext db,
    ServiceBusEventPublisher publisher,
    ILogger<RecordConsentHandler> logger)
{
    public async Task<Result<RecordConsentResult>> HandleAsync(
        RecordConsentCommand command,
        CancellationToken ct = default)
    {
        // ConsentRecord.Record hashes the IP immediately — raw IP never persisted
        var record = ConsentRecord.Record(
            command.TenantId,
            command.UserId,
            command.ConsentType,
            command.RawIpAddress);

        db.ConsentRecords.Add(record);
        await db.SaveChangesAsync(ct);

        foreach (var domainEvent in record.DomainEvents)
            await publisher.PublishAsync("crm.identity", domainEvent, ct);

        record.ClearDomainEvents();

        // No PII in logs — log only tenant + consent type
        logger.LogInformation(
            "Consent {ConsentType} recorded for user {UserId} in tenant {TenantId}",
            command.ConsentType, command.UserId, command.TenantId);

        return Result.Ok(new RecordConsentResult(record.Id));
    }
}
