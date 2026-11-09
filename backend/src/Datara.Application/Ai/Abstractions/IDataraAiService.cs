using Datara.Application.Analysis;

namespace Datara.Application.Ai.Abstractions;

/// <summary>Datara AI: natural language over a structured dataset context.</summary>
public interface IDataraAiService
{
    /// <summary>Produces a narrative summary plus separated facts and recommendations.</summary>
    Task<AiDatasetSummary> GenerateSummaryAsync(
        DatasetProfileResponse profile,
        string datasetName,
        CancellationToken cancellationToken);

    /// <summary>Answers a question grounded in the dataset's computed statistics.</summary>
    Task<AiChatResponse> AskAsync(
        DatasetProfileResponse profile,
        string datasetName,
        AiChatRequest request,
        CancellationToken cancellationToken);

    /// <summary>Reports whether a live model is configured and reachable.</summary>
    AiCapabilitiesDto GetCapabilities();
}