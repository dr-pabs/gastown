using CrmPlatform.IntegrationService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Domain;

namespace CrmPlatform.IntegrationService.Domain.Entities;

/// <summary>
/// Owned value object — retry policy for a connector.
/// Stored as owned entity (no separate table).
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>Give up after this many minutes of wall-clock time regardless of attempt count.</summary>
    public int MaxRetryDurationMinutes { get; private set; }

    /// <summary>Delay before the first retry.</summary>
    public int InitialRetryDelaySeconds { get; private set; }

    /// <summary>Maximum delay between retries (caps exponential growth).</summary>
    public int MaxRetryDelaySeconds { get; private set; }

    /// <summary>Exponential backoff multiplier.</summary>
    public double BackoffMultiplier { get; private set; }

    private RetryPolicy() { } // EF

    public static RetryPolicy Default() => new()
    {
        MaxRetryDurationMinutes  = 60,
        InitialRetryDelaySeconds = 30,
        MaxRetryDelaySeconds     = 300,
        BackoffMultiplier        = 2.0,
    };

    public static RetryPolicy Create(
        int maxRetryDurationMinutes,
        int initialRetryDelaySeconds,
        int maxRetryDelaySeconds,
        double backoffMultiplier)
    {
        if (maxRetryDurationMinutes <= 0)  throw new ArgumentOutOfRangeException(nameof(maxRetryDurationMinutes));
        if (initialRetryDelaySeconds <= 0) throw new ArgumentOutOfRangeException(nameof(initialRetryDelaySeconds));
        if (maxRetryDelaySeconds <= 0)     throw new ArgumentOutOfRangeException(nameof(maxRetryDelaySeconds));
        if (backoffMultiplier < 1.0)       throw new ArgumentOutOfRangeException(nameof(backoffMultiplier));

        return new()
        {
            MaxRetryDurationMinutes  = maxRetryDurationMinutes,
            InitialRetryDelaySeconds = initialRetryDelaySeconds,
            MaxRetryDelaySeconds     = maxRetryDelaySeconds,
            BackoffMultiplier        = backoffMultiplier,
        };
    }

    /// <summary>
    /// Calculates the next retry delay (in seconds) for a given attempt number (1-based).
    /// Returns null if the retry window has been exceeded.
    /// </summary>
    public int? NextDelaySeconds(int attemptNumber)
    {
        var delay = (int)(InitialRetryDelaySeconds * Math.Pow(BackoffMultiplier, attemptNumber - 1));
        return Math.Min(delay, MaxRetryDelaySeconds);
    }

    /// <summary>
    /// Returns true if the retry window has been exceeded given the first attempt time.
    /// </summary>
    public bool IsWindowExceeded(DateTime? firstAttemptAt)
    {
        if (firstAttemptAt is null) return false;
        return (DateTime.UtcNow - firstAttemptAt.Value).TotalMinutes >= MaxRetryDurationMinutes;
    }
}

/// <summary>
/// Per-tenant configuration for an external connector.
/// Owns credentials as a Key Vault secret name reference — never raw tokens.
/// Business rule: one active (non-Disconnected, non-Deleted) config per {TenantId, ConnectorType}.
/// </summary>
public sealed class ConnectorConfig : BaseEntity
{
    public ConnectorType   ConnectorType         { get; private set; }
    public ConnectorStatus Status                { get; private set; }

    /// <summary>Key Vault secret name, e.g. "integration-{tenantId}-hubspot". Null until connected.</summary>
    public string?   KeyVaultSecretName  { get; private set; }

    /// <summary>Space-separated OAuth2 scopes granted at connection time.</summary>
    public string?   OAuthScopes         { get; private set; }

    /// <summary>Access token expiry metadata (not the token itself — stored in KV).</summary>
    public DateTime? TokenExpiresUtc     { get; private set; }

    /// <summary>
    /// HMAC secret for inbound webhook signature validation.
    /// Stored in DB (not a credential — only used for signature verification).
    /// </summary>
    public string?   WebhookSecret       { get; private set; }

    /// <summary>External account identifier, e.g. Salesforce OrgId, HubSpot PortalId.</summary>
    public string?   ExternalAccountId   { get; private set; }

    /// <summary>Human-readable label set by the tenant admin.</summary>
    public string    Label               { get; private set; } = string.Empty;

    public string?   ErrorMessage        { get; private set; }

    // Owned value object — stored in same table
    public RetryPolicy RetryPolicy { get; private set; } = RetryPolicy.Default();

    private ConnectorConfig() { } // EF

    public static ConnectorConfig Create(
        Guid tenantId,
        ConnectorType connectorType,
        string label,
        RetryPolicy? retryPolicy = null)
    {
        return new ConnectorConfig
        {
            TenantId      = tenantId,
            ConnectorType = connectorType,
            Status        = ConnectorStatus.Disconnected,
            Label         = label.Trim(),
            RetryPolicy   = retryPolicy ?? RetryPolicy.Default(),
        };
    }

    /// <summary>
    /// Called after OAuth2 callback succeeds.
    /// </summary>
    public void Connect(
        string keyVaultSecretName,
        string externalAccountId,
        string? scopes,
        DateTime? tokenExpiry)
    {
        KeyVaultSecretName = keyVaultSecretName;
        ExternalAccountId  = externalAccountId;
        OAuthScopes        = scopes;
        TokenExpiresUtc    = tokenExpiry;
        Status             = ConnectorStatus.Connected;
        ErrorMessage       = null;
    }

    public void Disconnect()
    {
        Status             = ConnectorStatus.Disconnected;
        KeyVaultSecretName = null;
        ExternalAccountId  = null;
        OAuthScopes        = null;
        TokenExpiresUtc    = null;
        ErrorMessage       = null;
    }

    public void MarkError(string reason)
    {
        Status       = ConnectorStatus.Error;
        ErrorMessage = reason;
    }

    /// <summary>Platform admin only — e.g. tenant billing lapsed.</summary>
    public void Suspend()
    {
        Status     = ConnectorStatus.Suspended;
    }

    public void Reinstate()
    {
        if (Status != ConnectorStatus.Suspended)
            throw new InvalidOperationException("Only suspended connectors can be reinstated.");

        Status     = ConnectorStatus.Connected;
    }

    public void SetWebhookSecret(string secret)
    {
        WebhookSecret = secret;
    }

    public void UpdateRetryPolicy(RetryPolicy policy)
    {
        RetryPolicy = policy;
    }

    public bool IsActive => Status != ConnectorStatus.Disconnected && !IsDeleted;
}
