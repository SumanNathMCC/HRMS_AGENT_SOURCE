namespace HRMS_CHATBOT_SOURCE.Domain.Constants;

public static class KeyVaultSecretNames
{
    public const string AzureSqlSecret = "ConnectionString";
    public const string CosmosSecret = "Cosmos-Secret";
    public const string DocumentIntelligenceApiKey = "hrms-idp-03-api-key";
    public const string DocumentIntelligenceEndpoint = "hrms-idp-03-endpoint";
    public const string LlmApiKey = "mcc-lms-foundry-03-api-key";
    public const string LlmEndpoint = "mcc-lms-foundry-03-endpoint";
    public const string QdrantApiKey = "Qdrant-API-Key";
    public const string QdrantEndpoint = "Qdrant-EndPoint";
    public const string AzureSpeechApiKey = "mcc-lms-speech-api-key";
    public const string AzureSpeechEndpoint = "mcc-lms-speech-endpoint";
    public const string StorageConnectionString = "mcclmsstorage03-connection-string";

    public static readonly string[] All =
    [
        AzureSqlSecret,
        CosmosSecret,
        DocumentIntelligenceApiKey,
        DocumentIntelligenceEndpoint,
        LlmApiKey,
        LlmEndpoint,
        QdrantApiKey,
        QdrantEndpoint,
        AzureSpeechApiKey,
        AzureSpeechEndpoint,
        StorageConnectionString
    ];
}
