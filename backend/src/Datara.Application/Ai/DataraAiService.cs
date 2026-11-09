using System.Text;
using System.Text.Json;
using Datara.Application.Analysis;
using Datara.Application.Ai.Abstractions;
using Datara.Application.Common;
using Datara.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Datara.Application.Ai;

/// <summary>
/// Datara AI.
///
/// The governing rule: the model never sees the dataset. It sees a compact context
/// of computed statistics, and it must return structured output where every claim is
/// either a <c>fact</c> (traceable to a number in the context) or a <c>suggestion</c>.
/// When no provider is configured the same questions are answered deterministically
/// from the context, so the product is fully functional without an API key.
/// </summary>
public sealed class DataraAiService : IDataraAiService
{
    private readonly IDatasetContextBuilder _contextBuilder;
    private readonly ILlmProvider _provider;
    private readonly IDeterministicAiFallback _fallback;
    private readonly AiOptions _options;
    private readonly ILogger<DataraAiService> _logger;

    public DataraAiService(
        IDatasetContextBuilder contextBuilder,
        ILlmProvider provider,
        IDeterministicAiFallback fallback,
        AiOptions options,
        ILogger<DataraAiService> logger)
    {
        _contextBuilder = contextBuilder;
        _provider = provider;
        _fallback = fallback;
        _options = options;
        _logger = logger;
    }

    public AiCapabilitiesDto GetCapabilities() => new()
    {
        Enabled = _options.Enabled && _provider.IsConfigured,
        Provider = _options.Provider,
        Model = _options.Enabled ? _options.Model : "deterministic-rules",
        FallbackAvailable = true,
        Notice = _options.Enabled && _provider.IsConfigured
            ? $"Datara AI is using {_options.Provider} ({_options.Model}) to interpret "
              + "the calculated dataset context."
            : "No LLM provider is configured. Datara AI is answering from the calculated "
              + "dataset context using deterministic rules only.",
    };

    public async Task<AiDatasetSummary> GenerateSummaryAsync(
        DatasetProfileResponse profile,
        string datasetName,
        CancellationToken cancellationToken)
    {
        if (profile.MlReadiness is null)
        {
            throw new InvalidOperationException(
                "Cannot summarise a dataset that has not been analysed.");
        }

        var context = _contextBuilder.BuildContext(profile, datasetName);

        if (!_options.Enabled || !_provider.IsConfigured)
        {
            _logger.LogInformation(
                "Generating deterministic summary for dataset {DatasetName} (no LLM configured)",
                datasetName);
            return _fallback.BuildSummary(profile, datasetName);
        }

        var prompt = PromptBuilder.BuildSummaryPrompt(datasetName, context, _options.MaxOutputTokens);

        var completion = await _provider.CompleteAsync(
            prompt,
            _options.MaxOutputTokens,
            cancellationToken);

        if (completion is null)
        {
            _logger.LogWarning(
                "LLM provider returned no usable payload; falling back to deterministic summary");
            return _fallback.BuildSummary(profile, datasetName) with
            {
                Notice = "The AI provider did not return a usable response, so this summary "
                         + "was generated from calculated statistics only.",
            };
        }

        return _fallback.EnrichSummaryWithModel(
            profile,
            datasetName,
            completion,
            _options.Provider,
            _options.Model);
    }

    public async Task<AiChatResponse> AskAsync(
        DatasetProfileResponse profile,
        string datasetName,
        AiChatRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException("A question is required.", nameof(request));
        }

        var context = _contextBuilder.BuildContext(profile, datasetName);

        if (!_options.Enabled || !_provider.IsConfigured)
        {
            return _fallback.AnswerQuestion(profile, datasetName, request, context);
        }

        var prompt = PromptBuilder.BuildChatPrompt(
            datasetName,
            context,
            request,
            _options.MaxOutputTokens);

        var completion = await _provider.CompleteAsync(prompt, _options.MaxOutputTokens, cancellationToken);

        if (completion is null)
        {
            return _fallback.AnswerQuestion(profile, datasetName, request, context) with
            {
                Notice = "The AI provider did not return a usable response, so this answer "
                         + "was produced from calculated statistics only.",
            };
        }

        return _fallback.BuildChatResponse(
            profile,
            datasetName,
            request,
            completion,
            _options.Provider,
            _options.Model);
    }
}

/// <summary>Abstraction over an LLM provider. Implementations live in Infrastructure.</summary>
public interface ILlmProvider
{
    /// <summary>False when required configuration (for example an API key) is absent.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends a prompt and returns the raw text, or null when the provider responded
    /// with something unparseable. Implementations must not throw for model errors.
    /// </summary>
    Task<string?> CompleteAsync(string prompt, int maxOutputTokens, CancellationToken cancellationToken);
}

/// <summary>Rule-based answers used when no LLM is configured, or as a safety net.</summary>
public interface IDeterministicAiFallback
{
    AiDatasetSummary BuildSummary(DatasetProfileResponse profile, string datasetName);

    AiChatResponse AnswerQuestion(
        DatasetProfileResponse profile,
        string datasetName,
        AiChatRequest request,
        string context);

    /// <summary>
    /// Merges model output with facts recomputed from the profile. The facts list is
    /// always calculated, never model-generated.
    /// </summary>
    AiDatasetSummary EnrichSummaryWithModel(
        DatasetProfileResponse profile,
        string datasetName,
        string modelOutput,
        string provider,
        string model);

    AiChatResponse BuildChatResponse(
        DatasetProfileResponse profile,
        string datasetName,
        AiChatRequest request,
        string modelOutput,
        string provider,
        string model);
}