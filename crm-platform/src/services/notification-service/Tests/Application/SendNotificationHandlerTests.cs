using CrmPlatform.NotificationService.Application;
using CrmPlatform.NotificationService.Domain.Entities;
using CrmPlatform.NotificationService.Domain.Enums;
using CrmPlatform.NotificationService.Infrastructure.Acs;
using CrmPlatform.NotificationService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CrmPlatform.NotificationService.Tests.Application;

public sealed class SendNotificationHandlerTests
{
    private static NotificationDbContext BuildDb(Guid tenantId)
    {
        var opts = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new NotificationDbContext(opts, tenantId);
        db.Database.EnsureCreated();
        return db;
    }

    private static SendNotificationHandler BuildHandler(
        NotificationDbContext db,
        INotificationSender? sender = null,
        IEventPublisher? publisher = null)
    {
        sender    ??= Mock.Of<INotificationSender>();
        publisher ??= Mock.Of<IEventPublisher>();

        return new SendNotificationHandler(
            db,
            sender,
            publisher,
            NullLogger<SendNotificationHandler>.Instance);
    }

    // ─── Happy-path email send ────────────────────────────────────────────────

    [Fact]
    public async Task SendEmail_HappyPath_RecordStatusIsSent()
    {
        var tenantId = Guid.NewGuid();
        var userId   = Guid.NewGuid();
        var db       = BuildDb(tenantId);

        var senderMock = new Mock<INotificationSender>();
        senderMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("acs-msg-001");

        var handler = BuildHandler(db, senderMock.Object);

        var cmd = new SendNotificationCommand(
            TenantId:    tenantId,
            UserId:      userId,
            Address:     "jane@example.com",
            Channel:     NotificationChannel.Email,
            Category:    NotificationCategory.UserWelcome,
            Subject:     "Hello",
            Body:        "Welcome!",
            TemplateId:  null,
            Variables:   null);

        await handler.HandleAsync(cmd, CancellationToken.None);

        var record = await db.Records.FirstAsync();
        record.Status.Should().Be(NotificationStatus.Sent);
        record.ProviderMessageId.Should().Be("acs-msg-001");
    }

    // ─── Opted-out user ───────────────────────────────────────────────────────

    [Fact]
    public async Task Send_WhenOptedOut_RecordIsSkipped_SenderNotCalled()
    {
        var tenantId = Guid.NewGuid();
        var userId   = Guid.NewGuid();
        var db       = BuildDb(tenantId);

        // Seed opt-out preference
        var pref = NotificationPreference.Create(
            tenantId, userId,
            NotificationChannel.Email,
            NotificationCategory.General,
            isEnabled: false);

        db.Preferences.Add(pref);
        await db.SaveChangesAsync();

        var senderMock = new Mock<INotificationSender>();
        var handler    = BuildHandler(db, senderMock.Object);

        var cmd = new SendNotificationCommand(
            TenantId: tenantId, UserId: userId,
            Address: "jane@example.com",
            Channel: NotificationChannel.Email,
            Category: NotificationCategory.General,
            Subject: "test", Body: "test",
            TemplateId: null, Variables: null);

        await handler.HandleAsync(cmd, CancellationToken.None);

        var record = await db.Records.FirstAsync();
        record.Status.Should().Be(NotificationStatus.Skipped);
        senderMock.Verify(s => s.SendAsync(It.IsAny<NotificationRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── InApp channel bypasses preference check ──────────────────────────────

    [Fact]
    public async Task SendInApp_IgnoresOptOut_InAppNotificationCreated()
    {
        var tenantId = Guid.NewGuid();
        var userId   = Guid.NewGuid();
        var db       = BuildDb(tenantId);

        // Even with explicit opt-out, InApp should still be created
        db.Preferences.Add(NotificationPreference.Create(
            tenantId, userId,
            NotificationChannel.InApp,
            NotificationCategory.General,
            isEnabled: false));
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);

        var cmd = new SendNotificationCommand(
            TenantId: tenantId, UserId: userId,
            Address: userId.ToString(),
            Channel: NotificationChannel.InApp,
            Category: NotificationCategory.General,
            Subject: "You have a message", Body: "Details here",
            TemplateId: null, Variables: null);

        await handler.HandleAsync(cmd, CancellationToken.None);

        var inApp = await db.InAppItems.FirstAsync();
        inApp.RecipientUserId.Should().Be(userId);
        inApp.IsRead.Should().BeFalse();
    }

    // ─── ACS throws → record marked Failed ───────────────────────────────────

    [Fact]
    public async Task Send_WhenAcsThrows_RecordIsFailedAndEventPublished()
    {
        var tenantId  = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        var db        = BuildDb(tenantId);
        var pubMock   = new Mock<IEventPublisher>();

        var senderMock = new Mock<INotificationSender>();
        senderMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("ACS unavailable"));

        var handler = BuildHandler(db, senderMock.Object, pubMock.Object);

        var cmd = new SendNotificationCommand(
            TenantId: tenantId, UserId: userId,
            Address: "sms:+447700900000",
            Channel: NotificationChannel.Sms,
            Category: NotificationCategory.General,
            Subject: null, Body: "Test SMS",
            TemplateId: null, Variables: null);

        await handler.HandleAsync(cmd, CancellationToken.None);

        var record = await db.Records.FirstAsync();
        record.Status.Should().Be(NotificationStatus.Failed);
        record.FailureReason.Should().Contain("ACS unavailable");

        pubMock.Verify(
            p => p.PublishAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── Template rendering ───────────────────────────────────────────────────

    [Fact]
    public async Task Send_WithTemplate_RendersBodyFromTemplate()
    {
        var tenantId = Guid.NewGuid();
        var userId   = Guid.NewGuid();
        var db       = BuildDb(tenantId);

        var template = NotificationTemplate.Create(
            tenantId, "User Welcome",
            NotificationCategory.UserWelcome,
            NotificationChannel.Email,
            bodyPlainTemplate: "Hello {{firstName}}! Your account is ready.",
            subjectTemplate:   "Welcome {{firstName}}");

        template.Activate();
        db.Templates.Add(template);
        await db.SaveChangesAsync();

        var senderMock = new Mock<INotificationSender>();
        senderMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("acs-msg-999");

        var handler = BuildHandler(db, senderMock.Object);

        var cmd = new SendNotificationCommand(
            TenantId: tenantId, UserId: userId,
            Address: "user@example.com",
            Channel: NotificationChannel.Email,
            Category: NotificationCategory.UserWelcome,
            Subject: null, Body: null,
            TemplateId: template.Id,
            Variables: new Dictionary<string, object?> { ["firstName"] = "Alice" });

        await handler.HandleAsync(cmd, CancellationToken.None);

        var record = await db.Records.FirstAsync();
        record.Status.Should().Be(NotificationStatus.Sent);
        record.Body.Should().Contain("Alice");
        record.Subject.Should().Be("Welcome Alice");
    }
}
