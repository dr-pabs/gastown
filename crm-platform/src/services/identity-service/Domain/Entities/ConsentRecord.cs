using System.Security.Cryptography;
using System.Text;
using CrmPlatform.IdentityService.Domain.Enums;
using CrmPlatform.IdentityService.Domain.Events;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.IdentityService.Domain.Entities;

/// <summary>
/// Immutable GDPR consent record.
/// Rules:
///   - Never deleted or updated — consent history must be preserved forever.
///   - Raw IP address is NEVER stored. Only SHA-256 hash.
///   - IsDeleted is never set (base class SoftDelete() must not be called).
/// </summary>
public sealed class ConsentRecord : BaseEntity
{
    private ConsentRecord() { } // EF Core

    public Guid        TenantUserId  { get; private set; }
    public ConsentType ConsentType   { get; private set; }
    public DateTime    ConsentedAt   { get; private set; }
    public string      IpAddressHash { get; private set; } = string.Empty;

    // Navigation
    public TenantUser? TenantUser { get; private set; }

    /// <param name="rawIpAddress">Raw IP address — will be hashed immediately, never stored.</param>
    public static ConsentRecord Record(
        Guid tenantId,
        Guid tenantUserId,
        ConsentType consentType,
        string rawIpAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawIpAddress);

        var ipHash = HashIpAddress(rawIpAddress);

        var record = new ConsentRecord
        {
            TenantId     = tenantId,
            TenantUserId = tenantUserId,
            ConsentType  = consentType,
            ConsentedAt  = DateTime.UtcNow,
            IpAddressHash = ipHash,
        };

        record.AddDomainEvent(new ConsentRecordedEvent(tenantUserId, tenantId, consentType.ToString()));
        return record;
    }

    private static string HashIpAddress(string rawIp)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawIp));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
