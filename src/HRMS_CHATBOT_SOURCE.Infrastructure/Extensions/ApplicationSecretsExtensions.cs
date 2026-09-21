using HRMS_CHATBOT_SOURCE.Domain.Constants;
using HRMS_CHATBOT_SOURCE.Domain.Dto.Response;

namespace HRMS_CHATBOT_SOURCE.Infrastructure.Extensions;

public static class ApplicationSecretsExtensions
{
    public static IDictionary<string, string?> ToConfigurationDictionary(this ApplicationSecrets secrets)
    {
        var values = new Dictionary<string, string?>();

        AddIfPresent(values, "ConnectionStrings:HrmsDb", secrets.AzureSqlSecret);
        AddIfPresent(values, "ConnectionStrings:CosmosDb", secrets.CosmosSecret);
        AddIfPresent(values, "ConnectionStrings:AzureSql", secrets.AzureSqlSecret);

        var documentIntelligenceEndpoint = NormalizeDocumentIntelligenceEndpoint(secrets.DocumentIntelligenceEndpoint);
        AddIfPresent(values, "DocumentIntelligence:ApiKey", secrets.DocumentIntelligenceApiKey);
        AddIfPresent(values, "DocumentIntelligence:Endpoint", documentIntelligenceEndpoint);
        AddIfPresent(values, "DocumentExtraction:AzureDocumentIntelligence:ApiKey", secrets.DocumentIntelligenceApiKey);
        AddIfPresent(values, "DocumentExtraction:AzureDocumentIntelligence:Endpoint", documentIntelligenceEndpoint);

        if (!string.IsNullOrWhiteSpace(secrets.DocumentIntelligenceApiKey)
            && !string.IsNullOrWhiteSpace(documentIntelligenceEndpoint))
        {
            values["DocumentExtraction:AzureDocumentIntelligence:EnableAzureDocumentIntelligence"] = "true";
            values["DocumentExtraction:AzureDocumentIntelligence:ModelId"] = DocumentExtractionDefaults.AzureModelId;
        }
        AddIfPresent(values, "LLM:ApiKey", secrets.LlmApiKey);
        AddIfPresent(values, "LLM:Endpoint", secrets.LlmEndpoint);
        AddIfPresent(values, "LLM:OpenAIEndpoint", secrets.LlmOpenAiEndpoint);
        AddIfPresent(values, "AgentFoundry:ApiKey", secrets.LlmApiKey);
        AddIfPresent(values, "AgentFoundry:ProjectEndpoint", secrets.LlmEndpoint);
        AddIfPresent(values, "AgentFoundry:OpenAIEndpoint", secrets.LlmOpenAiEndpoint);
        AddIfPresent(values, "Qdrant:ApiKey", secrets.QdrantApiKey);
        AddIfPresent(values, "Qdrant:Endpoint", secrets.QdrantEndpoint);
        AddIfPresent(values, "Qdrant:Host", secrets.QdrantEndpoint);
        AddIfPresent(values, "AzureSpeech:ApiKey", secrets.AzureSpeechApiKey);
        AddIfPresent(values, "AzureSpeech:Endpoint", secrets.AzureSpeechEndpoint);
        AddIfPresent(values, "Storage:ConnectionString", secrets.StorageConnectionString);
        AddIfPresent(values, "Storage:AccountKey", ExtractAccountKey(secrets.StorageConnectionString));

        return values;
    }

    private static void AddIfPresent(IDictionary<string, string?> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value;
        }
    }

    private static string? ExtractAccountKey(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var segments = part.Split('=', 2);
            if (segments.Length == 2
                && string.Equals(segments[0].Trim(), "AccountKey", StringComparison.OrdinalIgnoreCase))
            {
                return segments[1].Trim();
            }
        }

        return null;
    }

    internal static string? NormalizeDocumentIntelligenceEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        var normalized = endpoint.Trim();
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            return normalized;
        }

        var path = uri.AbsolutePath.Trim('/');
        if (path.Contains("formrecognizer", StringComparison.OrdinalIgnoreCase)
            || path.Contains("documentintelligence", StringComparison.OrdinalIgnoreCase))
        {
            normalized = $"{uri.Scheme}://{uri.Authority}/";
        }
        else if (!normalized.EndsWith('/'))
        {
            normalized += "/";
        }

        return normalized;
    }
}
