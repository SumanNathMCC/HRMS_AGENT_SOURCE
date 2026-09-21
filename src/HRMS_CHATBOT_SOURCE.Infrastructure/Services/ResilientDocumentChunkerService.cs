using MCC.Foundation.Chunker;
using MCC.Foundation.Chunker.Abstractions;
using MCC.Foundation.Chunker.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRMS_CHATBOT_SOURCE.Infrastructure.Services;

/// <summary>
/// Tries Azure Document Intelligence first, then falls back to local extractors
/// (PdfPig, ClosedXML, OpenXml, etc.) when Azure returns no chunks or fails.
/// </summary>
public sealed class ResilientDocumentChunkerService : IDocumentChunkerService
{
    private readonly IDocumentChunkerService _primaryChunker;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ResilientDocumentChunkerService> _logger;
    private IDocumentChunkerService? _localChunker;

    public ResilientDocumentChunkerService(
        IDocumentChunkerService primaryChunker,
        IConfiguration configuration,
        ILogger<ResilientDocumentChunkerService> logger)
    {
        _primaryChunker = primaryChunker;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DocumentChunk>> ChunkDocumentAsync(
        DocumentChunkRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsAzureDocumentIntelligenceEnabled())
        {
            return await _primaryChunker.ChunkDocumentAsync(request, cancellationToken);
        }

        ResetStreamPosition(request.Content);

        try
        {
            var chunks = await _primaryChunker.ChunkDocumentAsync(request, cancellationToken);
            if (chunks is { Count: > 0 })
            {
                return chunks;
            }

            _logger.LogWarning(
                "Azure Document Intelligence returned no chunks for {FileName}; falling back to local extractors.",
                request.FileName);
        }
        catch (Exception ex) when (IsAzureDocumentIntelligenceFailure(ex))
        {
            _logger.LogWarning(
                ex,
                "Azure Document Intelligence failed for {FileName}; falling back to local extractors.",
                request.FileName);
        }

        ResetStreamPosition(request.Content);
        return await GetLocalChunker().ChunkDocumentAsync(request, cancellationToken);
    }

    private IDocumentChunkerService GetLocalChunker()
    {
        if (_localChunker != null)
        {
            return _localChunker;
        }

        var localConfiguration = new ConfigurationBuilder()
            .AddConfiguration(_configuration)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DocumentExtraction:PreferAzureDocumentIntelligence"] = "false",
                ["DocumentExtraction:AzureDocumentIntelligence:EnableAzureDocumentIntelligence"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDocumentChunker(localConfiguration);
        _localChunker = services
            .BuildServiceProvider()
            .GetRequiredService<IDocumentChunkerService>();

        return _localChunker;
    }

    private bool IsAzureDocumentIntelligenceEnabled()
    {
        return string.Equals(
            _configuration["DocumentExtraction:AzureDocumentIntelligence:EnableAzureDocumentIntelligence"],
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAzureDocumentIntelligenceFailure(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("Document Intelligence", StringComparison.OrdinalIgnoreCase)
                || message.Contains("DocumentIntelligence", StringComparison.OrdinalIgnoreCase)
                || message.Contains("BadRequest", StringComparison.OrdinalIgnoreCase)
                || message.Contains("API version", StringComparison.OrdinalIgnoreCase)
                || message.Contains("NotSupportedApiVersion", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void ResetStreamPosition(Stream? content)
    {
        if (content?.CanSeek == true)
        {
            content.Position = 0;
        }
    }
}
