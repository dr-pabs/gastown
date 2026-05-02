using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.MarketingService.Infrastructure.Data;

public sealed class MarketingIdempotencyRecord
{
    public string   MessageId   { get; init; } = string.Empty;
    public DateTime ProcessedAt { get; init; }
}

public sealed class MarketingIdempotencyStore(MarketingDbContext db) : IIdempotencyStore
{
    public async Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken ct = default)
        => await db.IdempotencyRecords
            .AsNoTracking()
            .AnyAsync(r => r.MessageId == messageId, ct);

    public async Task MarkProcessedAsync(string messageId, CancellationToken ct = default)
    {
        db.IdempotencyRecords.Add(new MarketingIdempotencyRecord
        {
            MessageId   = messageId,
            ProcessedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
