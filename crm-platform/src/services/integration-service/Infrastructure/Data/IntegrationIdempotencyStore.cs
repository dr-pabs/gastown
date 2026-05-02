using CrmPlatform.IntegrationService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.IntegrationService.Infrastructure.Data;

public sealed class IntegrationIdempotencyStore(IntegrationDbContext db) : IIdempotencyStore
{
    public async Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken ct = default)
    {
        return await db.IdempotencyRecords
            .AsNoTracking()
            .AnyAsync(r => r.MessageId == messageId, ct);
    }

    public async Task MarkProcessedAsync(string messageId, CancellationToken ct = default)
    {
        if (await HasBeenProcessedAsync(messageId, ct)) return;

        db.IdempotencyRecords.Add(new IntegrationIdempotencyRecord
        {
            MessageId   = messageId,
            ProcessedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }
}
