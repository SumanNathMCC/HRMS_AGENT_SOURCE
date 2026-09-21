using Azure;
using Azure.AI.OpenAI;
using HRMS_CHATBOT_SOURCE.Domain.Dto.Settings;
using HRMS_CHATBOT_SOURCE.Domain.Helpers;
using HRMS_CHATBOT_SOURCE.RAG.Abstractions;
using HRMS_CHATBOT_SOURCE.RAG.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;

namespace HRMS_CHATBOT_SOURCE.RAG.Services;

public class AzureFoundryEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _embeddingClient;
    private readonly IngestionSettings _ingestionSettings;
    private readonly ILogger<AzureFoundryEmbeddingService> _logger;

    public AzureFoundryEmbeddingService(
        IOptions<AgentFoundrySettings> foundryOptions,
        IOptions<IngestionSettings> ingestionOptions,
        IConfiguration configuration,
        ILogger<AzureFoundryEmbeddingService> logger)
    {
        _ingestionSettings = ingestionOptions.Value;
        _logger = logger;

        var foundry = foundryOptions.Value;
        var endpoint = AzureFoundryEndpointResolver.ResolveOpenAIEndpoint(configuration, foundry);
        var apiKey = FirstNonEmpty(
            foundry.ApiKey,
            configuration["AgentFoundry:ApiKey"],
            configuration["LLM:ApiKey"]);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("mcc-lms-foundry-03-api-key must be configured in Azure Key Vault for embeddings.");
        }

        var deployment = AzureFoundryEndpointResolver.ResolveEmbeddingDeployment(foundry, configuration);
        var apiVersion = FirstNonEmpty(
            foundry.ApiVersion,
            configuration["AgentFoundry:ApiVersion"],
            configuration["LLM:ApiVersion"]);

        if (string.IsNullOrWhiteSpace(apiVersion))
        {
            throw new InvalidOperationException("AgentFoundry:ApiVersion must be configured in appsettings for embeddings.");
        }

        var serviceVersion = AzureOpenAiServiceVersionResolver.Resolve(apiVersion);
        var clientOptions = new AzureOpenAIClientOptions(serviceVersion);
        var client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey), clientOptions);
        _embeddingClient = client.GetEmbeddingClient(deployment);

        _logger.LogInformation(
            "Embedding client configured for deployment {Deployment} at {Endpoint} using api-version {ApiVersion}.",
            deployment,
            endpoint,
            apiVersion);
    }

    public async Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0)
        {
            return [];
        }

        var results = new List<float[]>(inputs.Count);
        var batchSize = Math.Max(1, _ingestionSettings.BatchSize);

        for (var index = 0; index < inputs.Count; index += batchSize)
        {
            var batch = inputs.Skip(index).Take(batchSize).ToList();
            var response = await _embeddingClient.GenerateEmbeddingsAsync(batch, cancellationToken: cancellationToken);

            foreach (var embedding in response.Value)
            {
                results.Add(embedding.ToFloats().ToArray());
            }
        }

        _logger.LogDebug("Generated {EmbeddingCount} embeddings.", results.Count);

        return results;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
