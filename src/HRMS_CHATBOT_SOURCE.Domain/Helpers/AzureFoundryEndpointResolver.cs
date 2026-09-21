using HRMS_CHATBOT_SOURCE.Domain.Dto.Settings;
using Microsoft.Extensions.Configuration;

namespace HRMS_CHATBOT_SOURCE.Domain.Helpers;

public static class AzureFoundryEndpointResolver
{
    /// <summary>
    /// Embeddings must use the Azure OpenAI inference endpoint, not the Foundry project endpoint.
    /// See: https://learn.microsoft.com/en-us/azure/foundry/how-to/develop/sdk-overview
    /// </summary>
    public static string ResolveOpenAIEndpoint(IConfiguration configuration, AgentFoundrySettings? foundry = null)
    {
        var explicitEndpoint = FirstNonEmpty(
            foundry?.OpenAIEndpoint,
            configuration["AgentFoundry:OpenAIEndpoint"],
            configuration["LLM:OpenAIEndpoint"]);

        if (!string.IsNullOrWhiteSpace(explicitEndpoint))
        {
            return NormalizeOpenAIEndpoint(explicitEndpoint);
        }

        var sourceEndpoint = FirstNonEmpty(
            foundry?.ProjectEndpoint,
            configuration["LLM:Endpoint"],
            configuration["AgentFoundry:ProjectEndpoint"]);

        if (string.IsNullOrWhiteSpace(sourceEndpoint))
        {
            throw new InvalidOperationException(
                "LLM:Endpoint, AgentFoundry:ProjectEndpoint, or mcc-lms-foundry-03-endpoint (Key Vault) must be configured.");
        }

        return NormalizeOpenAIEndpoint(ConvertToOpenAIEndpoint(sourceEndpoint));
    }

    public static string ResolveEmbeddingDeployment(
        AgentFoundrySettings foundry,
        IConfiguration? configuration = null)
    {
        var deployment = FirstNonEmpty(
            foundry.EmbeddingDeployment,
            configuration?["AgentFoundry:EmbeddingDeployment"],
            configuration?["LLM:EmbeddingDeployment"],
            foundry.EmbeddingModel);

        if (string.IsNullOrWhiteSpace(deployment))
        {
            throw new InvalidOperationException(
                "AgentFoundry:EmbeddingModel must be set in appsettings.");
        }

        return deployment;
    }

    public static string ResolveChatDeployment(
        AgentFoundrySettings foundry,
        IConfiguration? configuration = null)
    {
        var deployment = FirstNonEmpty(
            foundry.ChatDeployment,
            configuration?["AgentFoundry:ChatDeployment"],
            configuration?["LLM:Deployment"],
            foundry.ChatModel);

        if (string.IsNullOrWhiteSpace(deployment))
        {
            throw new InvalidOperationException(
                "AgentFoundry:ChatModel must be set in appsettings.");
        }

        return deployment;
    }

    internal static string ConvertToOpenAIEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri))
        {
            return endpoint.Trim().TrimEnd('/');
        }

        if (uri.Host.Contains(".openai.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            return $"{uri.Scheme}://{uri.Host}";
        }

        // Foundry project endpoint: https://{resource}.services.ai.azure.com/api/projects/{project}
        if (uri.Host.EndsWith(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            return $"{uri.Scheme}://{uri.Host}";
        }

        return $"{uri.Scheme}://{uri.Host}";
    }

    private static string NormalizeOpenAIEndpoint(string endpoint)
    {
        var normalized = endpoint.Trim().TrimEnd('/');

        if (normalized.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^"/openai/v1".Length].TrimEnd('/');
        }

        return normalized;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
