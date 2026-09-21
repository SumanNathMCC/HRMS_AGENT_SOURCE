using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using HRMS_CHATBOT_SOURCE.Domain.Constants;
using HRMS_CHATBOT_SOURCE.Domain.Dto.Response;
using HRMS_CHATBOT_SOURCE.Domain.Dto.Settings;
using HRMS_CHATBOT_SOURCE.Infrastructure.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HRMS_CHATBOT_SOURCE.Domain.Json;
using Newtonsoft.Json;

namespace HRMS_CHATBOT_SOURCE.Infrastructure.Services;

public class AzureKeyVaultService : IAzureKeyVaultService
{
    private readonly AzureKeyVaultSettings _settings;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AzureKeyVaultService> _logger;

    public ApplicationSecrets Secrets { get; private set; }

    public AzureKeyVaultService(
        ApplicationSecrets secrets,
        IOptions<AzureKeyVaultSettings> settings,
        IHostEnvironment environment,
        ILogger<AzureKeyVaultService> logger)
    {
        Secrets = secrets;
        _settings = settings.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Secrets = await FetchSecretsAsync(cancellationToken);
        await PersistSecretsAsync(Secrets, cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
    }

    private async Task<ApplicationSecrets> FetchSecretsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.VaultUri))
        {
            _logger.LogWarning("Azure Key Vault URI is not configured. Loading secrets from local cache if available.");
            return await LoadCachedSecretsAsync(cancellationToken) ?? Secrets;
        }

        try
        {
            var client = CreateClient();
            var llmEndpoint = await GetSecretValueAsync(client, KeyVaultSecretNames.LlmEndpoint, cancellationToken);
            var secrets = new ApplicationSecrets
            {
                AzureSqlSecret = await GetSecretValueAsync(client, KeyVaultSecretNames.AzureSqlSecret, cancellationToken),
                CosmosSecret = await GetSecretValueAsync(client, KeyVaultSecretNames.CosmosSecret, cancellationToken),
                DocumentIntelligenceApiKey = await GetSecretValueAsync(client, KeyVaultSecretNames.DocumentIntelligenceApiKey, cancellationToken),
                DocumentIntelligenceEndpoint = await GetSecretValueAsync(client, KeyVaultSecretNames.DocumentIntelligenceEndpoint, cancellationToken),
                LlmApiKey = await GetSecretValueAsync(client, KeyVaultSecretNames.LlmApiKey, cancellationToken),
                LlmEndpoint = llmEndpoint,
                LlmOpenAiEndpoint = llmEndpoint,
                QdrantApiKey = await GetSecretValueAsync(client, KeyVaultSecretNames.QdrantApiKey, cancellationToken),
                QdrantEndpoint = await GetSecretValueAsync(client, KeyVaultSecretNames.QdrantEndpoint, cancellationToken),
                AzureSpeechApiKey = await GetSecretValueAsync(client, KeyVaultSecretNames.AzureSpeechApiKey, cancellationToken),
                AzureSpeechEndpoint = await GetSecretValueAsync(client, KeyVaultSecretNames.AzureSpeechEndpoint, cancellationToken),
                StorageConnectionString = await GetSecretValueAsync(client, KeyVaultSecretNames.StorageConnectionString, cancellationToken),
                FetchedAtUtc = DateTime.UtcNow
            };

            _logger.LogInformation("Successfully fetched {Count} secrets from Azure Key Vault.", KeyVaultSecretNames.All.Length);
            return secrets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch secrets from Azure Key Vault. Attempting to use cached secrets.");
            return await LoadCachedSecretsAsync(cancellationToken) ?? Secrets;
        }
    }

    private SecretClient CreateClient()
    {
        var vaultUri = new Uri(_settings.VaultUri);

        if (!_settings.UseManagedIdentity
            && !string.IsNullOrWhiteSpace(_settings.TenantId)
            && !string.IsNullOrWhiteSpace(_settings.ClientId)
            && !string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            return new SecretClient(vaultUri, new ClientSecretCredential(
                _settings.TenantId,
                _settings.ClientId,
                _settings.ClientSecret));
        }

        return new SecretClient(vaultUri, new DefaultAzureCredential());
    }

    private static async Task<string?> GetSecretValueAsync(
        SecretClient client,
        string secretName,
        CancellationToken cancellationToken)
    {
        var response = await client.GetSecretAsync(secretName, cancellationToken: cancellationToken);
        return response.Value.Value;
    }

    private async Task PersistSecretsAsync(ApplicationSecrets secrets, CancellationToken cancellationToken)
    {
        var cachePath = ResolveCachePath();
        var directory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonConvert.SerializeObject(secrets, Formatting.Indented, DomainJsonSerializerSettings.Default);
        await File.WriteAllTextAsync(cachePath, json, cancellationToken);
        _logger.LogInformation("Key Vault secrets cached at {CachePath}", cachePath);
    }

    private async Task<ApplicationSecrets?> LoadCachedSecretsAsync(CancellationToken cancellationToken)
    {
        var cachePath = ResolveCachePath();
        if (!File.Exists(cachePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(cachePath, cancellationToken);
        return JsonConvert.DeserializeObject<ApplicationSecrets>(json, DomainJsonSerializerSettings.Default);
    }

    private string ResolveCachePath()
    {
        return Path.IsPathRooted(_settings.SecretsCachePath)
            ? _settings.SecretsCachePath
            : Path.Combine(_environment.ContentRootPath, _settings.SecretsCachePath);
    }
}
