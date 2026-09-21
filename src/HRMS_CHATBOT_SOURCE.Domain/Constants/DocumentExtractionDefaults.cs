namespace HRMS_CHATBOT_SOURCE.Domain.Constants;

public static class DocumentExtractionDefaults
{
    /// <summary>
    /// Prebuilt layout model for Azure Document Intelligence v4 (SDK 2024-11-30).
    /// </summary>
    public const string AzureModelId = "prebuilt-layout";

    public const string ChunkerStrategy = "SectionAware";
}
