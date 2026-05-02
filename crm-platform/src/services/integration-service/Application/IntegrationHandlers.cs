using CrmPlatform.IntegrationService.Domain.Entities;
using CrmPlatform.IntegrationService.Domain.Enums;
using CrmPlatform.IntegrationService.Infrastructure.Connectors;
using CrmPlatform.IntegrationService.Infrastructure.Data;
using CrmPlatform.IntegrationService.Infrastructure.KeyVault;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace CrmPlatform.IntegrationService.Application;

// ─── OAuth2 handler ───────────────────────────────────────────────────────────

public sealed class OAuthOptions
{
    public string SalesforceClientId     { get; set; } = string.Empty;
    public string SalesforceClientSecret { get; set; } = string.Empty;  // from KV at startup
    public string SalesforceTokenUrl     { get; set; } = "https://login.salesforce.com/services/oauth2/token";
    public string SalesforceAuthUrl      { get; set; } = "https://login.salesforce.com/services/oauth2/authorize";

    public string HubSpotClientId        { get; set; } = string.Empty;
    public string HubSpotClientSecret    { get; set; } = string.Empty;  // from KV at startup
    public string HubSpotTokenUrl        { get; set; } = "https://api.hubapi.com/oauth/v1/token";
    public string HubSpotAuthUrl         { get; set; } = "https://app.hubspot.com/oauth/authorize";

    public string CallbackBaseUrl        { get; set; } = string.Empty;  // e.g. https://api.crm.example.com
    public string StaffPortalBaseUrl     { get; set; } = string.Empty;  // redirect after connect
}

public sealed class ConnectorOAuthHandler(
    IntegrationDbContext db,
    IConnectorTokenStore tokenStore,
    IHttpClientFactory httpClientFactory,
    IDistributedCache cache,
    IOptions<OAuthOptions> options,
    ILogger<ConnectorOAuthHandler> logger)
{
    private const string OAuthNonceCachePrefix = "oauth-nonce:";
    private const int    NonceTtlMinutes       = 10;

    /// <summary>
    /// Builds the authorization URL for the given connector. Returns it to the frontend
    /// which redirects the user's browser.
    /// </summary>
    public async Task<string> GetAuthorizationUrlAsync(ConnectorConfig config, CancellationToken ct)
    {
        var nonce = Guid.NewGuid().ToString("N");
        var state = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{config.Id}:{nonce}"));

        // Store nonce in distributed cache with 10-minute TTL
        await cache.SetStringAsync(
            $"{OAuthNonceCachePrefix}{config.Id}",
            nonce,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(NonceTtlMinutes) },
            ct);

        var callbackUrl = $"{options.Value.CallbackBaseUrl}/integrations/oauth/callback";

        return config.ConnectorType switch
        {
            ConnectorType.Salesforce => BuildSalesforceAuthUrl(state, callbackUrl),
            ConnectorType.HubSpot    => BuildHubSpotAuthUrl(state, callbackUrl),
            _ => throw new InvalidOperationException($"Connector type {config.ConnectorType} does not support OAuth2"),
        };
    }

    /// <summary>
    /// Handles the OAuth2 callback: validates state, exchanges code for tokens,
    /// stores refresh token in Key Vault, marks connector as Connected.
    /// Returns the staff portal redirect URL.
    /// </summary>
    public async Task<string> HandleCallbackAsync(string code, string state, CancellationToken ct)
    {
        var (connectorConfigId, nonce) = ParseState(state);

        var config = await db.Connectors.FindAsync([connectorConfigId], ct)
            ?? throw new InvalidOperationException("Connector config not found");

        // CSRF check — validate nonce from cache
        var storedNonce = await cache.GetStringAsync($"{OAuthNonceCachePrefix}{connectorConfigId}", ct);
        if (storedNonce != nonce)
            throw new InvalidOperationException("OAuth2 state mismatch — possible CSRF");

        await cache.RemoveAsync($"{OAuthNonceCachePrefix}{connectorConfigId}", ct);

        var (refreshToken, accessToken, expiresAt, scopes, externalAccountId) =
            await ExchangeCodeAsync(config.ConnectorType, code, ct);

        var secretName = $"integration-{config.TenantId}-{config.ConnectorType.ToString().ToLowerInvariant()}";
        await tokenStore.StoreRefreshTokenAsync(secretName, refreshToken, ct);

        config.Connect(secretName, externalAccountId, scopes, expiresAt);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Connector {ConnectorId} ({ConnectorType}) connected for tenant {TenantId}",
            config.Id, config.ConnectorType, config.TenantId);

        return $"{options.Value.StaffPortalBaseUrl}/settings/integrations/{config.ConnectorType.ToString().ToLowerInvariant()}?connected=true";
    }

    private string BuildSalesforceAuthUrl(string state, string callbackUrl)
    {
        var o = options.Value;
        return $"{o.SalesforceAuthUrl}?response_type=code&client_id={Uri.EscapeDataString(o.SalesforceClientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}&state={Uri.EscapeDataString(state)}&scope=api";
    }

    private string BuildHubSpotAuthUrl(string state, string callbackUrl)
    {
        var o = options.Value;
        return $"{o.HubSpotAuthUrl}?client_id={Uri.EscapeDataString(o.HubSpotClientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}&scope=crm.objects.contacts.read%20crm.objects.deals.read" +
               $"&state={Uri.EscapeDataString(state)}";
    }

    private async Task<(string RefreshToken, string AccessToken, DateTime ExpiresAt, string Scopes, string ExternalAccountId)>
        ExchangeCodeAsync(ConnectorType connectorType, string code, CancellationToken ct)
    {
        var o           = options.Value;
        var client      = httpClientFactory.CreateClient("oauth");
        var callbackUrl = $"{o.CallbackBaseUrl}/integrations/oauth/callback";

        var (tokenUrl, clientId, clientSecret) = connectorType switch
        {
            ConnectorType.Salesforce => (o.SalesforceTokenUrl, o.SalesforceClientId, o.SalesforceClientSecret),
            ConnectorType.HubSpot    => (o.HubSpotTokenUrl,    o.HubSpotClientId,    o.HubSpotClientSecret),
            _ => throw new InvalidOperationException($"OAuth not supported for {connectorType}"),
        };

        var form = new Dictionary<string, string>
        {
            ["grant_type"]    = "authorization_code",
            ["code"]          = code,
            ["redirect_uri"]  = callbackUrl,
            ["client_id"]     = clientId,
            ["client_secret"] = clientSecret,
        };

        var response = await client.PostAsync(tokenUrl, new FormUrlEncodedContent(form), ct);
        response.EnsureSuccessStatusCode();

        var json        = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root        = json.RootElement;
        var accessToken = root.GetProperty("access_token").GetString()!;
        var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString()! : accessToken;
        var expiresIn   = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
        var scopes      = root.TryGetProperty("scope", out var sc) ? sc.GetString() ?? "" : "";
        var externalId  = root.TryGetProperty("instance_url", out var iu) ? iu.GetString()
                        : root.TryGetProperty("hub_id", out var hi) ? hi.GetInt64().ToString()
                        : "unknown";

        return (refreshToken, accessToken, DateTime.UtcNow.AddSeconds(expiresIn), scopes, externalId!);
    }

    private static (Guid ConnectorConfigId, string Nonce) ParseState(string state)
    {
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(state));
        var parts   = decoded.Split(':', 2);
        if (parts.Length != 2) throw new InvalidOperationException("Invalid OAuth state");
        return (Guid.Parse(parts[0]), parts[1]);
    }
}

// ─── Connector management handler ─────────────────────────────────────────────

public sealed class ConnectorManagementHandler(
    IntegrationDbContext db,
    IConnectorTokenStore tokenStore,
    ServiceBusEventPublisher publisher,
    ILogger<ConnectorManagementHandler> logger)
{
    public async Task<ConnectorConfig> CreateAsync(
        Guid tenantId, ConnectorType connectorType, string label, RetryPolicy? retryPolicy, CancellationToken ct)
    {
        // Check for existing active config
        var existing = await db.Connectors
            .AnyAsync(c => c.ConnectorType == connectorType && !c.IsDeleted
                        && c.Status != ConnectorStatus.Disconnected, ct);

        if (existing)
            throw new InvalidOperationException(
                $"An active {connectorType} connector already exists for this tenant.");

        var config = ConnectorConfig.Create(tenantId, connectorType, label, retryPolicy);
        db.Connectors.Add(config);
        await db.SaveChangesAsync(ct);
        return config;
    }

    public async Task DisconnectAsync(Guid connectorId, CancellationToken ct)
    {
        var config = await db.Connectors.FindAsync([connectorId], ct)
            ?? throw new KeyNotFoundException("Connector not found");

        var secretName = config.KeyVaultSecretName;

        config.Disconnect();
        await db.SaveChangesAsync(ct);

        // Delete token from Key Vault
        if (!string.IsNullOrEmpty(secretName))
        {
            await tokenStore.DeleteAsync(secretName, ct);
        }

        publisher.Enqueue("crm.integrations", new Domain.Events.ConnectorDisconnectedEvent(
            config.TenantId, config.Id, config.ConnectorType.ToString(), "Manual disconnect"));

        await publisher.PublishPendingAsync(ct);

        logger.LogInformation("Connector {ConnectorId} disconnected for tenant {TenantId}",
            connectorId, config.TenantId);
    }

    public async Task DeleteAsync(Guid connectorId, CancellationToken ct)
    {
        var config = await db.Connectors.FindAsync([connectorId], ct)
            ?? throw new KeyNotFoundException("Connector not found");

        if (config.Status != ConnectorStatus.Disconnected)
            throw new InvalidOperationException("Connector must be Disconnected before deletion.");

        config.SoftDelete();
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateRetryPolicyAsync(Guid connectorId, RetryPolicy policy, CancellationToken ct)
    {
        var config = await db.Connectors.FindAsync([connectorId], ct)
            ?? throw new KeyNotFoundException("Connector not found");

        config.UpdateRetryPolicy(policy);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReplayJobAsync(Guid jobId, Guid tenantId, CancellationToken ct)
    {
        var original = await db.OutboundJobs.FindAsync([jobId], ct)
            ?? throw new KeyNotFoundException("Job not found");

        if (original.Status != OutboundJobStatus.Abandoned)
            throw new InvalidOperationException("Only Abandoned jobs can be replayed.");

        // Create a fresh job from the same payload — preserves original audit trail
        var replay = OutboundJob.Create(
            tenantId,
            original.ConnectorConfigId,
            original.ConnectorType,
            original.EventType,
            original.Payload);

        db.OutboundJobs.Add(replay);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Replayed job {OriginalJobId} → new job {NewJobId}", original.Id, replay.Id);
    }
}

// ─── Inbound webhook handler ──────────────────────────────────────────────────

public sealed class InboundWebhookHandler(
    IntegrationDbContext db,
    IEnumerable<IWebhookValidator> validators,
    ServiceBusEventPublisher publisher,
    ILogger<InboundWebhookHandler> logger)
{
    private readonly Dictionary<ConnectorType, IWebhookValidator> _validators =
        validators.ToDictionary(v => v.ConnectorType);

    public async Task<bool> HandleAsync(
        Guid tenantId, ConnectorType connectorType,
        HttpRequest request, string rawBody, CancellationToken ct)
    {
        // 1. Resolve connector config
        var config = await db.Connectors
            .FirstOrDefaultAsync(c => c.ConnectorType == connectorType
                                   && c.Status == ConnectorStatus.Connected, ct);

        if (config is null)
        {
            logger.LogWarning("Inbound webhook: no connected {ConnectorType} for tenant {TenantId}",
                connectorType, tenantId);
            return false; // caller returns 404
        }

        // 2. Validate signature
        if (_validators.TryGetValue(connectorType, out var validator))
        {
            if (!validator.Validate(request, rawBody, config.WebhookSecret))
            {
                logger.LogWarning("Inbound webhook signature validation failed for {ConnectorType} tenant {TenantId}",
                    connectorType, tenantId);
                return false; // caller returns 401
            }
        }

        // 3. Extract external event ID for deduplication
        var externalEventId = ExtractExternalEventId(connectorType, rawBody);

        // Check duplicate
        if (!string.IsNullOrEmpty(externalEventId))
        {
            var isDuplicate = await db.InboundEvents
                .AnyAsync(e => e.ExternalEventId == externalEventId, ct);
            if (isDuplicate)
            {
                logger.LogInformation("Duplicate inbound event {ExternalEventId} — skipped", externalEventId);
                return true; // idempotent — return 202
            }
        }

        // 4. Persist raw event
        var inboundEvent = InboundEvent.Receive(tenantId, connectorType, externalEventId, rawBody);
        var normalisedType = NormaliseEventType(connectorType, rawBody);
        inboundEvent.SetNormalisedType(normalisedType);

        db.InboundEvents.Add(inboundEvent);

        // 5. Publish to crm.integrations
        var domainEvent = new Domain.Events.ExternalEventReceivedEvent(
            TenantId:            tenantId,
            InboundEventId:      inboundEvent.Id,
            ConnectorType:       connectorType.ToString(),
            NormalisedEventType: normalisedType,
            PayloadSummary:      BuildPayloadSummary(connectorType, rawBody));

        publisher.Enqueue("crm.integrations", domainEvent);

        await db.SaveChangesAsync(ct);
        await publisher.PublishPendingAsync(ct);

        inboundEvent.MarkPublished(domainEvent.EventId.ToString());
        await db.SaveChangesAsync(ct);

        return true;
    }

    private static string? ExtractExternalEventId(ConnectorType connectorType, string rawBody)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(rawBody);
            return connectorType switch
            {
                ConnectorType.HubSpot => doc.RootElement
                    .TryGetProperty("eventId", out var id) ? id.GetInt64().ToString() : null,
                ConnectorType.Salesforce => doc.RootElement
                    .TryGetProperty("id", out var sfId) ? sfId.GetString() : null,
                _ => null,
            };
        }
        catch { return null; }
    }

    private static string NormaliseEventType(ConnectorType connectorType, string rawBody)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(rawBody);
            var prefix = connectorType.ToString().ToLowerInvariant();
            var subType = connectorType switch
            {
                ConnectorType.HubSpot => doc.RootElement
                    .TryGetProperty("subscriptionType", out var st) ? st.GetString() : "unknown",
                ConnectorType.Salesforce => doc.RootElement
                    .TryGetProperty("changeType", out var ct2) ? ct2.GetString() : "unknown",
                _ => "payload",
            };
            return $"{prefix}.{subType}";
        }
        catch { return $"{connectorType.ToString().ToLowerInvariant()}.unknown"; }
    }

    private static string BuildPayloadSummary(ConnectorType connectorType, string rawBody)
    {
        // Return only non-PII summary for telemetry
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(rawBody);
            return connectorType switch
            {
                ConnectorType.HubSpot => doc.RootElement
                    .TryGetProperty("objectId", out var oid) ? $"ObjectId:{oid}" : "HubSpot event",
                _ => $"{connectorType} event",
            };
        }
        catch { return "Unknown event"; }
    }
}
