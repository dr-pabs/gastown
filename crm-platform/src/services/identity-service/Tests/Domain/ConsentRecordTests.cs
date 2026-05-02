using CrmPlatform.IdentityService.Domain.Entities;
using CrmPlatform.IdentityService.Domain.Enums;
using CrmPlatform.IdentityService.Domain.Events;
using FluentAssertions;

namespace CrmPlatform.IdentityService.Tests.Domain;

public sealed class ConsentRecordTests
{
    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId  = Guid.NewGuid();

    [Fact]
    public void Record_HashesIpAddress()
    {
        var record = ConsentRecord.Record(TenantA, UserId, ConsentType.PrivacyPolicy, "192.168.1.1");

        record.IpAddressHash.Should().NotBe("192.168.1.1",
            "raw IP must never be stored");
        record.IpAddressHash.Should().HaveLength(64, "SHA-256 hex string is 64 chars");
    }

    [Fact]
    public void Record_SameIpAlwaysProducesSameHash()
    {
        var record1 = ConsentRecord.Record(TenantA, UserId, ConsentType.PrivacyPolicy, "10.0.0.1");
        var record2 = ConsentRecord.Record(TenantA, UserId, ConsentType.TermsOfService, "10.0.0.1");

        record1.IpAddressHash.Should().Be(record2.IpAddressHash);
    }

    [Fact]
    public void Record_PublishesConsentRecordedEvent()
    {
        var record = ConsentRecord.Record(TenantA, UserId, ConsentType.MarketingEmails, "1.2.3.4");

        record.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ConsentRecordedEvent>();
    }

    [Fact]
    public void Record_ThrowsIfIpIsEmpty()
    {
        var act = () => ConsentRecord.Record(TenantA, UserId, ConsentType.PrivacyPolicy, "");

        act.Should().Throw<ArgumentException>();
    }
}
