using Microsoft.EntityFrameworkCore;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.PlatformAdminService.Infrastructure.Data;

public sealed class PlatformIdempotencyRecord
{
    public string   MessageId   { get; init; } = string.Empty;
    public DateTime ProcessedAt { get; init; }
}

public sealed class PlatformIdempotencyStore(PlatformDbContext db) : IIdempotencyStore
{
    public async Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken ct = default) =>
        await db.IdempotencyRecords.AnyAsync(r => r.MessageId == messageId, ct);

    public async Task MarkProcessedAsync(string messageId, CancellationToken ct = default)
    {
        db.IdempotencyRecords.Add(new PlatformIdempotencyRecord
        {
            MessageId   = messageId,
            ProcessedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}
