using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using CrmPlatform.IntegrationService.Infrastructure.Connectors;
using Microsoft.Extensions.Options;

namespace CrmPlatform.IntegrationService.Infrastructure.KeyVault;

public sealed class KeyVaultOptions
{
    public string VaultUri { get; set; } = string.Empty;
}

/// <summary>
/// Stores and retrieves OAuth refresh tokens (and other connector credentials)
/// from Azure Key Vault using Managed Identity.
/// NEVER stores raw tokens in the database.
/// </summary>
public sealed class KeyVaultConnectorTokenStore(
    IOptions<KeyVaultOptions> options,
    ILogger<KeyVaultConnectorTokenStore> logger)
    : IConnectorTokenStore
{
    // Lazy — avoids creating the client if KV is not configured (e.g. test environments)
    private SecretClient? _client;

    private SecretClient Client => _client ??= new SecretClient(
        new Uri(options.Value.VaultUri),
        new DefaultAzureCredential());

    public async Task<string?> GetRefreshTokenAsync(string secretName, CancellationToken ct)
    {
        try
        {
            var response = await Client.GetSecretAsync(secretName, cancellationToken: ct);
            return response.Value.Value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            logger.LogWarning("Key Vault secret {SecretName} not found", secretName);
            return null;
        }
    }

    public async Task StoreRefreshTokenAsync(string secretName, string refreshToken, CancellationToken ct)
    {
        await Client.SetSecretAsync(secretName, refreshToken, ct);
        logger.LogInformation("Stored connector token in Key Vault: {SecretName}", secretName);
    }

    public async Task DeleteAsync(string secretName, CancellationToken ct)
    {
        try
        {
            await Client.StartDeleteSecretAsync(secretName, ct);
            logger.LogInformation("Deleted connector token from Key Vault: {SecretName}", secretName);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // Already deleted — idempotent
            logger.LogWarning("Key Vault secret {SecretName} not found during delete", secretName);
        }
    }
}
