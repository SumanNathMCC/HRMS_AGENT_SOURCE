using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using HRMS_CHATBOT_SOURCE.Domain.Constants;
using HRMS_CHATBOT_SOURCE.Domain.Dto.Response;
using HRMS_CHATBOT_SOURCE.Domain.Dto.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using HRMS_CHATBOT_SOURCE.Domain.Json;
using Newtonsoft.Json;

namespace HRMS_CHATBOT_SOURCE.Infrastructure.Bootstrap;

public static class KeyVaultBootstrap
{
    public static async Task<ApplicationSecrets> LoadSecretsAsync(
        IConfiguration configuration,
        IHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        var settings = configuration.GetSection(AzureKeyVaultSettings.SectionName).Get<AzureKeyVaultSettings>()
            ?? new AzureKeyVaultSettings();

        var cachePath = ResolveCachePath(settings.SecretsCachePath, environment.ContentRootPath);

        if (string.IsNullOrWhiteSpace(settings.VaultUri))
        {
            return await LoadFromCacheAsync(cachePath, cancellationToken) ?? new ApplicationSecrets();
        }

        try
        {
            var client = CreateClient(settings);
            var llmEndpoint = await GetSecretAsync(client, KeyVaultSecretNames.LlmEndpoint, cancellationToken);
            var secrets = new ApplicationSecrets
            {
                AzureSqlSecret = await GetSecretAsync(client, KeyVaultSecretNames.AzureSqlSecret, cancellationToken),
                CosmosSecret = await GetSecretAsync(client, KeyVaultSecretNames.CosmosSecret, cancellationToken),
                DocumentIntelligenceApiKey = await GetSecretAsync(client, KeyVaultSecretNames.DocumentIntelligenceApiKey, cancellationToken),
                DocumentIntelligenceEndpoint = await GetSecretAsync(client, KeyVaultSecretNames.DocumentIntelligenceEndpoint, cancellationToken),
                LlmApiKey = await GetSecretAsync(client, KeyVaultSecretNames.LlmApiKey, cancellationToken),
                LlmEndpoint = llmEndpoint,
                LlmOpenAiEndpoint = llmEndpoint,
                QdrantApiKey = await GetSecretAsync(client, KeyVaultSecretNames.QdrantApiKey, cancellationToken),
                QdrantEndpoint = await GetSecretAsync(client, KeyVaultSecretNames.QdrantEndpoint, cancellationToken),
                AzureSpeechApiKey = await GetSecretAsync(client, KeyVaultSecretNames.AzureSpeechApiKey, cancellationToken),
                AzureSpeechEndpoint = await GetSecretAsync(client, KeyVaultSecretNames.AzureSpeechEndpoint, cancellationToken),
                StorageConnectionString = await GetSecretAsync(client, KeyVaultSecretNames.StorageConnectionString, cancellationToken),
                FetchedAtUtc = DateTime.UtcNow
            };

            await PersistAsync(secrets, cachePath, cancellationToken);
            return secrets;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Key Vault fetch failed ({settings.VaultUri}): {ex.Message}. Using cached secrets if available.");
            return await LoadFromCacheAsync(cachePath, cancellationToken) ?? new ApplicationSecrets();
        }
    }

    private static SecretClient CreateClient(AzureKeyVaultSettings settings)
    {
        var vaultUri = new Uri(settings.VaultUri);

        if (!settings.UseManagedIdentity
            && !string.IsNullOrWhiteSpace(settings.TenantId)
            && !string.IsNullOrWhiteSpace(settings.ClientId)
            && !string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            return new SecretClient(vaultUri, new ClientSecretCredential(
                settings.TenantId,
                settings.ClientId,
                settings.ClientSecret));
        }

        return new SecretClient(vaultUri, new DefaultAzureCredential());
    }

    private static async Task<string?> GetSecretAsync(
        SecretClient client,
        string secretName,
        CancellationToken cancellationToken)
    {
        var response = await client.GetSecretAsync(secretName, cancellationToken: cancellationToken);
        return response.Value.Value;
    }

    private static async Task PersistAsync(
        ApplicationSecrets secrets,
        string cachePath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonConvert.SerializeObject(secrets, Formatting.Indented, DomainJsonSerializerSettings.Default);
        await File.WriteAllTextAsync(cachePath, json, cancellationToken);
    }

    private static async Task<ApplicationSecrets?> LoadFromCacheAsync(
        string cachePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(cachePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(cachePath, cancellationToken);
        return JsonConvert.DeserializeObject<ApplicationSecrets>(json, DomainJsonSerializerSettings.Default);
    }

    private static string ResolveCachePath(string configuredPath, string contentRootPath)
    {
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(contentRootPath, configuredPath);
    }
}
