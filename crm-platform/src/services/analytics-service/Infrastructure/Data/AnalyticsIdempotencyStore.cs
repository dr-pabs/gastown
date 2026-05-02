using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.AnalyticsService.Infrastructure.Data;

public sealed class AnalyticsIdempotencyRecord
{
    public string   MessageId   { get; init; } = string.Empty;
    public DateTime ProcessedAt { get; init; }
}

public sealed class AnalyticsIdempotencyStore(AnalyticsDbContext db) : IIdempotencyStore
{
    public async Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken ct = default)
        => await db.IdempotencyRecords
            .AsNoTracking()
            .AnyAsync(r => r.MessageId == messageId, ct);

    public async Task MarkProcessedAsync(string messageId, CancellationToken ct = default)
    {
        db.IdempotencyRecords.Add(new AnalyticsIdempotencyRecord
        {
            MessageId   = messageId,
            ProcessedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
