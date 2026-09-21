using Newtonsoft.Json;

namespace HRMS_CHATBOT_SOURCE.Domain.Dto.Response;

public class ApplicationSecrets
{
    [JsonProperty("azure_sql_secret")]
    public string? AzureSqlSecret { get; set; }

    [JsonProperty("cosmos_secret")]
    public string? CosmosSecret { get; set; }

    [JsonProperty("document_intelligence_api_key")]
    public string? DocumentIntelligenceApiKey { get; set; }

    [JsonProperty("document_intelligence_endpoint")]
    public string? DocumentIntelligenceEndpoint { get; set; }

    [JsonProperty("llm_api_key")]
    public string? LlmApiKey { get; set; }

    [JsonProperty("llm_endpoint")]
    public string? LlmEndpoint { get; set; }

    [JsonProperty("llm_openai_endpoint")]
    public string? LlmOpenAiEndpoint { get; set; }

    [JsonProperty("llm_api_version")]
    public string? LlmApiVersion { get; set; }

    [JsonProperty("llm_deployment")]
    public string? LlmDeployment { get; set; }

    [JsonProperty("llm_embedding_deployment")]
    public string? LlmEmbeddingDeployment { get; set; }

    [JsonProperty("qdrant_api_key")]
    public string? QdrantApiKey { get; set; }

    [JsonProperty("qdrant_endpoint")]
    public string? QdrantEndpoint { get; set; }

    [JsonProperty("azure_ai_search_admin_key")]
    public string? AzureAiSearchAdminKey { get; set; }

    [JsonProperty("azure_ai_search_endpoint")]
    public string? AzureAiSearchEndpoint { get; set; }

    [JsonProperty("azure_speech_api_key")]
    public string? AzureSpeechApiKey { get; set; }

    [JsonProperty("azure_speech_endpoint")]
    public string? AzureSpeechEndpoint { get; set; }

    [JsonProperty("storage_connection_string")]
    public string? StorageConnectionString { get; set; }

    [JsonProperty("fetched_at_utc")]
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
}
