using Microsoft.EntityFrameworkCore;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.IdentityService.Infrastructure.Data;

/// <summary>EF Core entity backing the identity-service idempotency store.</summary>
public sealed class IdempotencyRecord
{
    public string   MessageId   { get; init; } = string.Empty;
    public DateTime ProcessedAt { get; init; }
}

/// <summary>
/// EF Core implementation of IIdempotencyStore for the identity-service.
/// Stores processed Service Bus message IDs in identity.IdempotencyStore.
/// </summary>
public sealed class IdentityIdempotencyStore(IdentityDbContext db) : IIdempotencyStore
{
    public async Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken ct = default) =>
        await db.IdempotencyRecords.AnyAsync(r => r.MessageId == messageId, ct);

    public async Task MarkProcessedAsync(string messageId, CancellationToken ct = default)
    {
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            MessageId   = messageId,
            ProcessedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}
