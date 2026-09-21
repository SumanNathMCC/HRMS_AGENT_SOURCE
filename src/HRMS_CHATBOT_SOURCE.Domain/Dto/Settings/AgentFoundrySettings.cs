using Newtonsoft.Json;

namespace HRMS_CHATBOT_SOURCE.Domain.Dto.Settings;

public class AgentFoundrySettings
{
    public const string SectionName = "AgentFoundry";

    [JsonProperty("project_endpoint")]
    public string? ProjectEndpoint { get; set; }

    [JsonProperty("openai_endpoint")]
    public string? OpenAIEndpoint { get; set; }

    [JsonProperty("api_key")]
    public string? ApiKey { get; set; }

    [JsonProperty("api_version")]
    public string? ApiVersion { get; set; } = "2024-10-21";

    [JsonProperty("chat_model")]
    public string ChatModel { get; set; } = "gpt-4.1";

    [JsonProperty("chat_deployment")]
    public string? ChatDeployment { get; set; }

    [JsonProperty("embedding_model")]
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    [JsonProperty("embedding_deployment")]
    public string? EmbeddingDeployment { get; set; }
}
