using CrmPlatform.IntegrationService.Domain.Entities;
using CrmPlatform.IntegrationService.Domain.Enums;

namespace CrmPlatform.IntegrationService.Infrastructure.Connectors;

/// <summary>
/// Result returned by a connector adapter after attempting an outbound dispatch.
/// </summary>
public sealed record AdapterResult(
    bool    Success,
    string? ExternalId,
    string? FailureReason);

/// <summary>
/// Implemented by each connector type that supports outbound dispatch.
/// </summary>
public interface IConnectorAdapter
{
    ConnectorType ConnectorType { get; }

    Task<AdapterResult> SendAsync(
        OutboundJob    job,
        ConnectorConfig config,
        string?        accessToken,    // Fresh access token fetched from KV by the worker
        CancellationToken ct);
}

/// <summary>
/// Validates inbound webhook signatures.
/// </summary>
public interface IWebhookValidator
{
    ConnectorType ConnectorType { get; }

    /// <returns>True if signature is valid.</returns>
    bool Validate(HttpRequest request, string rawBody, string? secret);
}

// ─── Key Vault token helper ────────────────────────────────────────────────────

/// <summary>
/// Abstracts Key Vault secret retrieval for connector OAuth tokens.
/// </summary>
public interface IConnectorTokenStore
{
    Task<string?> GetRefreshTokenAsync(string secretName, CancellationToken ct);
    Task StoreRefreshTokenAsync(string secretName, string refreshToken, CancellationToken ct);
    Task DeleteAsync(string secretName, CancellationToken ct);
}

// ─── Salesforce adapter (outbound only at v1) ─────────────────────────────────

public sealed class SalesforceAdapter(
    IHttpClientFactory httpClientFactory,
    IConnectorTokenStore tokenStore,
    ILogger<SalesforceAdapter> logger)
    : IConnectorAdapter
{
    public ConnectorType ConnectorType => ConnectorType.Salesforce;

    public async Task<AdapterResult> SendAsync(
        OutboundJob job,
        ConnectorConfig config,
        string? accessToken,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(accessToken))
            return new AdapterResult(false, null, "No access token available");

        try
        {
            var client = httpClientFactory.CreateClient("salesforce");
            // Map eventType → Salesforce REST endpoint
            // e.g. lead.assigned → POST /services/data/v58.0/sobjects/Lead/
            var endpoint = MapEventToEndpoint(job.EventType);
            if (endpoint is null)
            {
                logger.LogWarning("No Salesforce mapping for event type {EventType} — skipping", job.EventType);
                return new AdapterResult(true, null, null); // not a failure — just not mapped
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(job.Payload, System.Text.Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                return new AdapterResult(false, null, $"HTTP {(int)response.StatusCode}: {body[..Math.Min(500, body.Length)]}");
            }

            // Extract Salesforce Id from response
            var result = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var sfId = result.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            return new AdapterResult(true, sfId, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Salesforce adapter error for job {JobId}", job.Id);
            return new AdapterResult(false, null, ex.Message);
        }
    }

    private static string? MapEventToEndpoint(string eventType) => eventType switch
    {
        "lead.assigned"     => "/services/data/v58.0/sobjects/Lead/",
        "opportunity.won"   => "/services/data/v58.0/sobjects/Opportunity/",
        "case.created"      => "/services/data/v58.0/sobjects/Case/",
        _                   => null,
    };
}

// ─── HubSpot adapter ──────────────────────────────────────────────────────────

public sealed class HubSpotAdapter(
    IHttpClientFactory httpClientFactory,
    ILogger<HubSpotAdapter> logger)
    : IConnectorAdapter
{
    public ConnectorType ConnectorType => ConnectorType.HubSpot;

    public async Task<AdapterResult> SendAsync(
        OutboundJob job,
        ConnectorConfig config,
        string? accessToken,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(accessToken))
            return new AdapterResult(false, null, "No access token available");

        try
        {
            var client   = httpClientFactory.CreateClient("hubspot");
            var endpoint = MapEventToEndpoint(job.EventType);
            if (endpoint is null)
            {
                logger.LogWarning("No HubSpot mapping for event type {EventType}", job.EventType);
                return new AdapterResult(true, null, null);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(job.Payload, System.Text.Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                return new AdapterResult(false, null, $"HTTP {(int)response.StatusCode}: {body[..Math.Min(500, body.Length)]}");
            }

            return new AdapterResult(true, null, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HubSpot adapter error for job {JobId}", job.Id);
            return new AdapterResult(false, null, ex.Message);
        }
    }

    private static string? MapEventToEndpoint(string eventType) => eventType switch
    {
        "lead.assigned"   => "/crm/v3/objects/contacts",
        "opportunity.won" => "/crm/v3/objects/deals",
        _                 => null,
    };
}

// ─── Azure Event Hub adapter ──────────────────────────────────────────────────

public sealed class AzureEventHubAdapter(
    IConnectorTokenStore tokenStore,
    ILogger<AzureEventHubAdapter> logger)
    : IConnectorAdapter
{
    public ConnectorType ConnectorType => ConnectorType.AzureEventHub;

    public async Task<AdapterResult> SendAsync(
        OutboundJob job,
        ConnectorConfig config,
        string? accessToken,
        CancellationToken ct)
    {
        try
        {
            // Connection string stored in KV (not OAuth — Event Hub uses SAS or Managed Identity)
            var connectionString = await tokenStore.GetRefreshTokenAsync(config.KeyVaultSecretName!, ct);
            if (string.IsNullOrEmpty(connectionString))
                return new AdapterResult(false, null, "Event Hub connection string not found in Key Vault");

            await using var producer = new Azure.Messaging.EventHubs.Producer.EventHubProducerClient(connectionString);
            var eventData = new Azure.Messaging.EventHubs.EventData(System.Text.Encoding.UTF8.GetBytes(job.Payload));
            eventData.Properties["eventType"]  = job.EventType;
            eventData.Properties["tenantId"]   = job.TenantId.ToString();

            using var batch = await producer.CreateBatchAsync(ct);
            if (!batch.TryAdd(eventData))
                return new AdapterResult(false, null, "Event payload too large for Event Hub batch");

            await producer.SendAsync(batch, ct);
            return new AdapterResult(true, null, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Event Hub adapter error for job {JobId}", job.Id);
            return new AdapterResult(false, null, ex.Message);
        }
    }
}

// ─── Webhook validators ───────────────────────────────────────────────────────

public sealed class HubSpotWebhookValidator : IWebhookValidator
{
    public ConnectorType ConnectorType => ConnectorType.HubSpot;

    public bool Validate(HttpRequest request, string rawBody, string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return false;

        if (!request.Headers.TryGetValue("X-HubSpot-Signature-v3", out var signatureHeader))
            return false;

        // HubSpot v3: HMAC-SHA256 of {clientSecret}{requestBody}
        var data     = System.Text.Encoding.UTF8.GetBytes(secret + rawBody);
        var expected = Convert.ToHexString(
            System.Security.Cryptography.HMACSHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(secret), data)).ToLowerInvariant();

        return string.Equals(signatureHeader.ToString(), expected, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class GenericWebhookValidator : IWebhookValidator
{
    public ConnectorType ConnectorType => ConnectorType.GenericWebhook;

    public bool Validate(HttpRequest request, string rawBody, string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return false;

        if (!request.Headers.TryGetValue("X-Webhook-Signature", out var signatureHeader))
            return false;

        var expected = Convert.ToHexString(
            System.Security.Cryptography.HMACSHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(secret),
                System.Text.Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();

        return string.Equals(signatureHeader.ToString(), expected, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class SalesforceWebhookValidator : IWebhookValidator
{
    // Stub — inbound Salesforce webhooks optional at v1
    public ConnectorType ConnectorType => ConnectorType.Salesforce;

    public bool Validate(HttpRequest request, string rawBody, string? secret) => true;
}
