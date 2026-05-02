using CrmPlatform.IntegrationService.Application;
using CrmPlatform.IntegrationService.Domain.Entities;
using CrmPlatform.IntegrationService.Domain.Enums;
using CrmPlatform.IntegrationService.Infrastructure.Connectors;
using CrmPlatform.IntegrationService.Infrastructure.Data;
using CrmPlatform.IntegrationService.Infrastructure.KeyVault;
using CrmPlatform.ServiceTemplate.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CrmPlatform.IntegrationService.Tests.Application;

// ─── Shared context factory ────────────────────────────────────────────────────

internal static class DbContextFactory
{
    public static IntegrationDbContext Create(Guid tenantId, string? dbName = null)
    {
        var opts = new DbContextOptionsBuilder<IntegrationDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;
        var accessor = new Mock<ITenantContextAccessor>();
        accessor.Setup(a => a.TenantId).Returns(tenantId);
        return new IntegrationDbContext(opts, accessor.Object);
    }
}

// ─── ConnectorManagementHandler tests ────────────────────────────────────────

public sealed class ConnectorManagementHandlerTests : IDisposable
{
    private readonly IntegrationDbContext _db;
    private readonly Mock<IConnectorTokenStore> _tokenStoreMock;
    private readonly Mock<ServiceBusEventPublisher> _publisherMock;
    private readonly ConnectorManagementHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();

    public ConnectorManagementHandlerTests()
    {
        _db             = DbContextFactory.Create(_tenantId);
        _tokenStoreMock = new Mock<IConnectorTokenStore>();
        _publisherMock  = new Mock<ServiceBusEventPublisher>(MockBehavior.Loose);

        _handler = new ConnectorManagementHandler(
            _db,
            _tokenStoreMock.Object,
            _publisherMock.Object,
            NullLogger<ConnectorManagementHandler>.Instance);
    }

    // ─── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_HappyPath_ReturnsDisconnectedConfig()
    {
        var config = await _handler.CreateAsync(
            _tenantId, ConnectorType.HubSpot, "My HubSpot", null, CancellationToken.None);

        config.Should().NotBeNull();
        config.Status.Should().Be(ConnectorStatus.Disconnected);
        config.ConnectorType.Should().Be(ConnectorType.HubSpot);
        config.Label.Should().Be("My HubSpot");
        config.RetryPolicy.Should().NotBeNull();

        // Verify persisted
        var persisted = await _db.Connectors.FindAsync(config.Id);
        persisted.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAsync_DuplicateActiveConnector_Throws()
    {
        // Arrange: create and connect a HubSpot connector
        var existing = ConnectorConfig.Create(_tenantId, ConnectorType.HubSpot, "Existing");
        existing.Connect("kv-secret", "portal-999", "crm.read", DateTime.UtcNow.AddHours(1));
        _db.Connectors.Add(existing);
        await _db.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.CreateAsync(_tenantId, ConnectorType.HubSpot, "Duplicate", null, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_WithCustomRetryPolicy_PersistsPolicy()
    {
        var policy = RetryPolicy.Create(120, 60, 600, 3.0);
        var config = await _handler.CreateAsync(
            _tenantId, ConnectorType.Salesforce, "SF", policy, CancellationToken.None);

        config.RetryPolicy.MaxRetryDurationMinutes.Should().Be(120);
    }

    // ─── Disconnect ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DisconnectAsync_HappyPath_DeletesKvSecretAndDisconnects()
    {
        // Arrange
        var config = ConnectorConfig.Create(_tenantId, ConnectorType.HubSpot, "HubSpot");
        config.Connect("integration-tenant-hubspot", "portal-123", "crm.read", DateTime.UtcNow.AddHours(1));
        _db.Connectors.Add(config);
        await _db.SaveChangesAsync();

        _tokenStoreMock
            .Setup(s => s.DeleteAsync("integration-tenant-hubspot", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.DisconnectAsync(config.Id, CancellationToken.None);

        // Assert
        var updated = await _db.Connectors.FindAsync(config.Id);
        updated!.Status.Should().Be(ConnectorStatus.Disconnected);
        updated.KeyVaultSecretName.Should().BeNull();

        _tokenStoreMock.Verify(
            s => s.DeleteAsync("integration-tenant-hubspot", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DisconnectAsync_NotFound_Throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _handler.DisconnectAsync(Guid.NewGuid(), CancellationToken.None));
    }

    // ─── UpdateRetryPolicy ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRetryPolicyAsync_UpdatesPolicyOnConnector()
    {
        var config = ConnectorConfig.Create(_tenantId, ConnectorType.HubSpot, "HubSpot");
        _db.Connectors.Add(config);
        await _db.SaveChangesAsync();

        var newPolicy = RetryPolicy.Create(120, 60, 600, 3.0);
        await _handler.UpdateRetryPolicyAsync(config.Id, newPolicy, CancellationToken.None);

        var updated = await _db.Connectors.FindAsync(config.Id);
        updated!.RetryPolicy.MaxRetryDurationMinutes.Should().Be(120);
    }

    // ─── ReplayJob ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplayJobAsync_FromAbandoned_CreatesNewQueuedJob()
    {
        var connectorId = Guid.NewGuid();
        var original = OutboundJob.Create(_tenantId, connectorId, ConnectorType.HubSpot, "lead.assigned", "{\"id\":1}");
        original.MarkInProgress();
        original.Abandon("Retry window exceeded");
        _db.OutboundJobs.Add(original);
        await _db.SaveChangesAsync();

        await _handler.ReplayJobAsync(original.Id, _tenantId, CancellationToken.None);

        var jobs = await _db.OutboundJobs.ToListAsync();
        jobs.Should().HaveCount(2);
        var replay = jobs.Single(j => j.Id != original.Id);
        replay.Status.Should().Be(OutboundJobStatus.Queued);
        replay.EventType.Should().Be("lead.assigned");
    }

    [Fact]
    public async Task ReplayJobAsync_FromNonAbandonedJob_Throws()
    {
        var job = OutboundJob.Create(_tenantId, Guid.NewGuid(), ConnectorType.HubSpot, "lead.assigned", "{}");
        _db.OutboundJobs.Add(job);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.ReplayJobAsync(job.Id, _tenantId, CancellationToken.None));
    }

    public void Dispose() => _db.Dispose();
}

// ─── InboundWebhookHandler tests ─────────────────────────────────────────────

public sealed class InboundWebhookHandlerTests : IDisposable
{
    private readonly IntegrationDbContext _db;
    private readonly Mock<ServiceBusEventPublisher> _publisherMock;
    private readonly Mock<IWebhookValidator> _validatorMock;
    private readonly InboundWebhookHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();

    public InboundWebhookHandlerTests()
    {
        _db            = DbContextFactory.Create(_tenantId);
        _publisherMock = new Mock<ServiceBusEventPublisher>(MockBehavior.Loose);
        _validatorMock = new Mock<IWebhookValidator>();
        _validatorMock.Setup(v => v.ConnectorType).Returns(ConnectorType.HubSpot);

        _handler = new InboundWebhookHandler(
            _db,
            new[] { _validatorMock.Object },
            _publisherMock.Object,
            NullLogger<InboundWebhookHandler>.Instance);
    }

    private async Task<ConnectorConfig> SeedConnectedHubSpotConnector()
    {
        var config = ConnectorConfig.Create(_tenantId, ConnectorType.HubSpot, "HubSpot");
        config.Connect("kv-secret", "portal-123", "crm.read", DateTime.UtcNow.AddHours(1));
        config.SetWebhookSecret("hmac-secret");
        _db.Connectors.Add(config);
        await _db.SaveChangesAsync();
        return config;
    }

    [Fact]
    public async Task HandleAsync_ValidSignature_PersistsInboundEvent()
    {
        await SeedConnectedHubSpotConnector();

        var rawBody = """{"eventId":9001,"subscriptionType":"contact.creation","objectId":42}""";
        var requestMock = new Mock<HttpRequest>();
        requestMock.Setup(r => r.Headers).Returns(new HeaderDictionary());

        _validatorMock
            .Setup(v => v.Validate(It.IsAny<HttpRequest>(), rawBody, "hmac-secret"))
            .Returns(true);

        var result = await _handler.HandleAsync(
            _tenantId, ConnectorType.HubSpot, requestMock.Object, rawBody, CancellationToken.None);

        result.Should().BeTrue();
        var events = await _db.InboundEvents.ToListAsync();
        events.Should().HaveCount(1);
        events[0].ExternalEventId.Should().Be("9001");
        events[0].Status.Should().Be(InboundEventStatus.Published);
    }

    [Fact]
    public async Task HandleAsync_InvalidSignature_ReturnsFalse()
    {
        await SeedConnectedHubSpotConnector();

        var rawBody = """{"eventId":9002,"subscriptionType":"contact.creation","objectId":43}""";
        var requestMock = new Mock<HttpRequest>();
        requestMock.Setup(r => r.Headers).Returns(new HeaderDictionary());

        _validatorMock
            .Setup(v => v.Validate(It.IsAny<HttpRequest>(), rawBody, "hmac-secret"))
            .Returns(false);

        var result = await _handler.HandleAsync(
            _tenantId, ConnectorType.HubSpot, requestMock.Object, rawBody, CancellationToken.None);

        result.Should().BeFalse();
        var events = await _db.InboundEvents.ToListAsync();
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_NoConnectedConnector_ReturnsFalse()
    {
        // No connector seeded
        var rawBody = """{"eventId":9003}""";
        var requestMock = new Mock<HttpRequest>();
        requestMock.Setup(r => r.Headers).Returns(new HeaderDictionary());

        var result = await _handler.HandleAsync(
            _tenantId, ConnectorType.HubSpot, requestMock.Object, rawBody, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_DuplicateExternalEventId_ReturnsTrueButDoesNotPersist()
    {
        await SeedConnectedHubSpotConnector();

        // Persist a prior event with same externalEventId
        var priorEvent = InboundEvent.Receive(_tenantId, ConnectorType.HubSpot, "9001", "{}");
        _db.InboundEvents.Add(priorEvent);
        await _db.SaveChangesAsync();

        var rawBody = """{"eventId":9001,"subscriptionType":"contact.creation","objectId":42}""";
        var requestMock = new Mock<HttpRequest>();
        requestMock.Setup(r => r.Headers).Returns(new HeaderDictionary());

        _validatorMock
            .Setup(v => v.Validate(It.IsAny<HttpRequest>(), rawBody, "hmac-secret"))
            .Returns(true);

        var result = await _handler.HandleAsync(
            _tenantId, ConnectorType.HubSpot, requestMock.Object, rawBody, CancellationToken.None);

        result.Should().BeTrue();
        var events = await _db.InboundEvents.ToListAsync();
        events.Should().HaveCount(1); // Only the original, no duplicate added
    }

    public void Dispose() => _db.Dispose();
}
