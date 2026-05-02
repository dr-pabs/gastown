using CrmPlatform.NotificationService.Domain.Entities;
using CrmPlatform.NotificationService.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace CrmPlatform.NotificationService.Tests.Domain;

public sealed class NotificationTemplateTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsDefaults()
    {
        var t = NotificationTemplate.Create(
            TenantId, "Welcome Email",
            NotificationCategory.UserWelcome,
            NotificationChannel.Email,
            "Hi {{name}}, welcome!",
            subjectTemplate: "Welcome {{name}}");

        t.Name.Should().Be("Welcome Email");
        t.IsActive.Should().BeFalse();
        t.Version.Should().Be(1);
        t.Channel.Should().Be(NotificationChannel.Email);
    }

    [Fact]
    public void Render_SubstitutesHandlebarsVariables()
    {
        var t = NotificationTemplate.Create(
            TenantId, "Test",
            NotificationCategory.General,
            NotificationChannel.Email,
            "Hello {{firstName}} {{lastName}}",
            subjectTemplate: "Hi {{firstName}}");

        var rendered = t.Render(new Dictionary<string, object?>
        {
            ["firstName"] = "Jane",
            ["lastName"]  = "Doe"
        });

        rendered.Subject.Should().Be("Hi Jane");
        rendered.BodyPlain.Should().Be("Hello Jane Doe");
    }

    [Fact]
    public void UpdateContent_WhileInactive_BumpsVersion()
    {
        var t = NotificationTemplate.Create(
            TenantId, "T", NotificationCategory.General,
            NotificationChannel.Email, "v1 body");

        t.UpdateContent("v2 body");

        t.BodyPlainTemplate.Should().Be("v2 body");
        t.Version.Should().Be(2);
    }

    [Fact]
    public void UpdateContent_WhenActive_Throws()
    {
        var t = NotificationTemplate.Create(
            TenantId, "T", NotificationCategory.General,
            NotificationChannel.Email, "body");

        t.Activate();

        var act = () => t.UpdateContent("new body");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Activate_SetsIsActiveTrue()
    {
        var t = NotificationTemplate.Create(
            TenantId, "T", NotificationCategory.General,
            NotificationChannel.Email, "body");

        t.Activate();
        t.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_TrimsWhitespaceFromName()
    {
        var t = NotificationTemplate.Create(
            TenantId, "  padded  ", NotificationCategory.General,
            NotificationChannel.Sms, "body");

        t.Name.Should().Be("padded");
    }
}

public sealed class NotificationRecordTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId   = Guid.NewGuid();

    [Fact]
    public void CreateQueued_SetsStatusQueued()
    {
        var r = NotificationRecord.CreateQueued(
            TenantId, UserId, "test@example.com",
            NotificationChannel.Email, NotificationCategory.UserWelcome,
            "Hello");

        r.Status.Should().Be(NotificationStatus.Queued);
        r.SentAt.Should().BeNull();
    }

    [Fact]
    public void MarkSent_SetsStatusAndProviderId()
    {
        var r = NotificationRecord.CreateQueued(
            TenantId, UserId, "test@example.com",
            NotificationChannel.Email, NotificationCategory.General, "body");

        r.MarkSent("acs-msg-123");

        r.Status.Should().Be(NotificationStatus.Sent);
        r.ProviderMessageId.Should().Be("acs-msg-123");
        r.SentAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateStatus_Delivered_SetsDeliveredAt()
    {
        var r = NotificationRecord.CreateQueued(
            TenantId, UserId, "test@example.com",
            NotificationChannel.Email, NotificationCategory.General, "body");

        r.MarkSent("msg-1");
        r.UpdateStatus(DeliveryWebhookEvent.Delivered);

        r.Status.Should().Be(NotificationStatus.Delivered);
        r.DeliveredAt.Should().NotBeNull();
    }

    [Fact]
    public void CreateSkipped_SetsStatusSkipped()
    {
        var r = NotificationRecord.CreateSkipped(
            TenantId, UserId, "test@example.com",
            NotificationChannel.Sms, NotificationCategory.General);

        r.Status.Should().Be(NotificationStatus.Skipped);
    }
}

public sealed class InAppNotificationTests
{
    [Fact]
    public void Create_SetsIsReadFalse()
    {
        var n = InAppNotification.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            "Case assigned", "Case #12345 assigned to you",
            NotificationCategory.CaseAssigned);

        n.IsRead.Should().BeFalse();
        n.ReadAt.Should().BeNull();
    }

    [Fact]
    public void MarkRead_SetsIsReadAndTimestamp()
    {
        var n = InAppNotification.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            "Title", "Body", NotificationCategory.General);

        n.MarkRead();

        n.IsRead.Should().BeTrue();
        n.ReadAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkRead_Idempotent_DoesNotThrow()
    {
        var n = InAppNotification.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            "T", "B", NotificationCategory.General);

        n.MarkRead();
        var firstReadAt = n.ReadAt;

        n.MarkRead(); // second call — should not update ReadAt
        n.ReadAt.Should().Be(firstReadAt);
    }
}
