using CrmPlatform.IntegrationService.Domain.Entities;
using CrmPlatform.IntegrationService.Domain.Enums;
using Xunit;

namespace CrmPlatform.IntegrationService.Tests.Domain;

public sealed class IntegrationDomainTests
{
    // ─── RetryPolicy ──────────────────────────────────────────────────────────

    [Fact]
    public void RetryPolicy_Default_HasExpectedValues()
    {
        var policy = RetryPolicy.Default();
        Assert.Equal(60,  policy.MaxRetryDurationMinutes);
        Assert.Equal(30,  policy.InitialRetryDelaySeconds);
        Assert.Equal(300, policy.MaxRetryDelaySeconds);
        Assert.Equal(2.0, policy.BackoffMultiplier);
    }

    [Fact]
    public void RetryPolicy_NextDelaySeconds_IsExponentialAndCapped()
    {
        var policy = RetryPolicy.Create(60, 30, 300, 2.0);
        Assert.Equal(30,  policy.NextDelaySeconds(1));  // 30 * 2^0
        Assert.Equal(60,  policy.NextDelaySeconds(2));  // 30 * 2^1
        Assert.Equal(120, policy.NextDelaySeconds(3));  // 30 * 2^2
        Assert.Equal(240, policy.NextDelaySeconds(4));  // 30 * 2^3
        Assert.Equal(300, policy.NextDelaySeconds(5));  // 30 * 2^4 = 480, capped at 300
    }

    [Fact]
    public void RetryPolicy_IsWindowExceeded_ReturnsTrueWhenExpired()
    {
        var policy     = RetryPolicy.Create(60, 30, 300, 2.0);
        var firstTry   = DateTime.UtcNow.AddMinutes(-61);
        Assert.True(policy.IsWindowExceeded(firstTry));
    }

    [Fact]
    public void RetryPolicy_IsWindowExceeded_ReturnsFalseWhenWithinWindow()
    {
        var policy   = RetryPolicy.Create(60, 30, 300, 2.0);
        var firstTry = DateTime.UtcNow.AddMinutes(-30);
        Assert.False(policy.IsWindowExceeded(firstTry));
    }

    [Fact]
    public void RetryPolicy_Create_ThrowsOnInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RetryPolicy.Create(0, 30, 300, 2.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RetryPolicy.Create(60, 0, 300, 2.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RetryPolicy.Create(60, 30, 0,   2.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RetryPolicy.Create(60, 30, 300, 0.5));
    }

    // ─── ConnectorConfig ──────────────────────────────────────────────────────

    [Fact]
    public void ConnectorConfig_Create_IsDisconnected()
    {
        var config = ConnectorConfig.Create(Guid.NewGuid(), ConnectorType.HubSpot, "My HubSpot");
        Assert.Equal(ConnectorStatus.Disconnected, config.Status);
        Assert.Null(config.KeyVaultSecretName);
        Assert.False(config.IsActive);
    }

    [Fact]
    public void ConnectorConfig_Connect_SetsStatusAndSecretName()
    {
        var config = ConnectorConfig.Create(Guid.NewGuid(), ConnectorType.HubSpot, "HubSpot");
        config.Connect("integration-tenant-hubspot", "portal-123", "crm.read", DateTime.UtcNow.AddHours(1));

        Assert.Equal(ConnectorStatus.Connected, config.Status);
        Assert.Equal("integration-tenant-hubspot", config.KeyVaultSecretName);
        Assert.Equal("portal-123",                 config.ExternalAccountId);
        Assert.True(config.IsActive);
    }

    [Fact]
    public void ConnectorConfig_Disconnect_ClearsCredentials()
    {
        var config = ConnectorConfig.Create(Guid.NewGuid(), ConnectorType.HubSpot, "HubSpot");
        config.Connect("secret-name", "portal-123", "crm.read", DateTime.UtcNow.AddHours(1));
        config.Disconnect();

        Assert.Equal(ConnectorStatus.Disconnected, config.Status);
        Assert.Null(config.KeyVaultSecretName);
        Assert.Null(config.ExternalAccountId);
        Assert.False(config.IsActive);
    }

    [Fact]
    public void ConnectorConfig_Suspend_SetsStatusSuspended()
    {
        var config = ConnectorConfig.Create(Guid.NewGuid(), ConnectorType.Salesforce, "SF");
        config.Connect("secret", "org-id", null, null);
        config.Suspend();
        Assert.Equal(ConnectorStatus.Suspended, config.Status);
    }

    [Fact]
    public void ConnectorConfig_Reinstate_RequiresSuspended()
    {
        var config = ConnectorConfig.Create(Guid.NewGuid(), ConnectorType.Salesforce, "SF");
        // Not suspended → must throw
        Assert.Throws<InvalidOperationException>(() => config.Reinstate());
    }

    [Fact]
    public void ConnectorConfig_Reinstate_FromSuspended_Succeeds()
    {
        var config = ConnectorConfig.Create(Guid.NewGuid(), ConnectorType.Salesforce, "SF");
        config.Connect("secret", "org-id", null, null);
        config.Suspend();
        config.Reinstate();
        Assert.Equal(ConnectorStatus.Connected, config.Status);
        Assert.True(config.IsActive);
    }

    [Fact]
    public void ConnectorConfig_UpdateRetryPolicy_Persists()
    {
        var config     = ConnectorConfig.Create(Guid.NewGuid(), ConnectorType.HubSpot, "HubSpot");
        var newPolicy  = RetryPolicy.Create(120, 60, 600, 3.0);
        config.UpdateRetryPolicy(newPolicy);
        Assert.Equal(120, config.RetryPolicy.MaxRetryDurationMinutes);
        Assert.Equal(60,  config.RetryPolicy.InitialRetryDelaySeconds);
    }

    [Fact]
    public void ConnectorConfig_SetWebhookSecret_Persists()
    {
        var config = ConnectorConfig.Create(Guid.NewGuid(), ConnectorType.GenericWebhook, "My Webhook");
        config.SetWebhookSecret("super-secret-hmac");
        Assert.Equal("super-secret-hmac", config.WebhookSecret);
    }

    [Fact]
    public void ConnectorConfig_MarkError_SetsStatusAndMessage()
    {
        var config = ConnectorConfig.Create(Guid.NewGuid(), ConnectorType.Salesforce, "SF");
        config.MarkError("token refresh failed");
        Assert.Equal(ConnectorStatus.Error, config.Status);
        Assert.Equal("token refresh failed", config.ErrorMessage);
    }

    // ─── OutboundJob ─────────────────────────────────────────────────────────

    [Fact]
    public void OutboundJob_Create_IsQueued()
    {
        var job = OutboundJob.Create(
            Guid.NewGuid(), Guid.NewGuid(), ConnectorType.HubSpot, "lead.assigned", "{}");

        Assert.Equal(OutboundJobStatus.Queued, job.Status);
        Assert.Equal(0, job.AttemptCount);
        Assert.Null(job.FirstAttemptAt);
    }

    [Fact]
    public void OutboundJob_MarkInProgress_IncrementsAttemptCount()
    {
        var job = OutboundJob.Create(
            Guid.NewGuid(), Guid.NewGuid(), ConnectorType.HubSpot, "lead.assigned", "{}");

        job.MarkInProgress();
        Assert.Equal(OutboundJobStatus.InProgress, job.Status);
        Assert.Equal(1, job.AttemptCount);
        Assert.NotNull(job.FirstAttemptAt);

        // Simulate fail + retry
        var firstAttemptAt = job.FirstAttemptAt;
        job.MarkFailed("transient error", DateTime.UtcNow.AddSeconds(30));
        job.MarkInProgress();
        Assert.Equal(2, job.AttemptCount);
        // FirstAttemptAt should NOT change on subsequent attempts
        Assert.Equal(firstAttemptAt, job.FirstAttemptAt);
    }

    [Fact]
    public void OutboundJob_MarkFailed_SetsNextRetryAt()
    {
        var job = OutboundJob.Create(
            Guid.NewGuid(), Guid.NewGuid(), ConnectorType.HubSpot, "lead.assigned", "{}");
        job.MarkInProgress();
        var nextRetry = DateTime.UtcNow.AddSeconds(60);
        job.MarkFailed("connection timeout", nextRetry);

        Assert.Equal(OutboundJobStatus.Failed, job.Status);
        Assert.Equal("connection timeout", job.FailureReason);
        Assert.Equal(nextRetry, job.NextRetryAt);
    }

    [Fact]
    public void OutboundJob_MarkSucceeded_IsTerminal()
    {
        var job = OutboundJob.Create(
            Guid.NewGuid(), Guid.NewGuid(), ConnectorType.Salesforce, "opportunity.won", "{}");
        job.MarkInProgress();
        job.MarkSucceeded("sf-record-123");

        Assert.Equal(OutboundJobStatus.Succeeded, job.Status);
        Assert.Equal("sf-record-123", job.ExternalId);
        Assert.True(job.IsTerminal);
        Assert.Null(job.NextRetryAt);
    }

    [Fact]
    public void OutboundJob_Abandon_IsTerminal_ClearsNextRetryAt()
    {
        var job = OutboundJob.Create(
            Guid.NewGuid(), Guid.NewGuid(), ConnectorType.HubSpot, "case.created", "{}");
        job.MarkInProgress();
        job.MarkFailed("err", DateTime.UtcNow.AddSeconds(30));
        job.Abandon("Retry window exceeded");

        Assert.Equal(OutboundJobStatus.Abandoned, job.Status);
        Assert.NotNull(job.AbandonedAt);
        Assert.True(job.IsTerminal);
        Assert.Null(job.NextRetryAt);
    }

    [Fact]
    public void RetryPolicy_IsWindowExceeded_FalseWhenFirstAttemptNull()
    {
        var policy = RetryPolicy.Default();
        Assert.False(policy.IsWindowExceeded(null));
    }

    // ─── InboundEvent ─────────────────────────────────────────────────────────

    [Fact]
    public void InboundEvent_Receive_IsReceived()
    {
        var ev = InboundEvent.Receive(Guid.NewGuid(), ConnectorType.HubSpot, "evt-123", "{\"objectId\":1}");
        Assert.Equal(InboundEventStatus.Received, ev.Status);
        Assert.Equal("evt-123", ev.ExternalEventId);
    }

    [Fact]
    public void InboundEvent_MarkPublished_SetsMessageId()
    {
        var ev = InboundEvent.Receive(Guid.NewGuid(), ConnectorType.HubSpot, "evt-123", "{}");
        ev.MarkPublished("sb-msg-abc");
        Assert.Equal(InboundEventStatus.Published, ev.Status);
        Assert.Equal("sb-msg-abc", ev.ServiceBusMessageId);
        Assert.NotNull(ev.ProcessedAt);
    }

    [Fact]
    public void InboundEvent_Skip_IsIdempotent()
    {
        var ev = InboundEvent.Receive(Guid.NewGuid(), ConnectorType.HubSpot, "evt-dup", "{}");
        ev.Skip("Duplicate event");
        Assert.Equal(InboundEventStatus.Skipped, ev.Status);
    }
}
