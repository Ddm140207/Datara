using System.Text.Json;
using Datara.Application.Ai;
using Microsoft.Extensions.Logging;

namespace Datara.Infrastructure.Ai;

/// <summary>
/// Datara AI provider for OpenAI and any OpenAI-compatible endpoint (vLLM,
/// LM Studio, OpenRouter, Together, Groq) via <c>Ai:BaseUrl</c>.
/// </summary>
public sealed class OpenAiChatProvider : HttpChatProvider
{
    private readonly AiOptions _options;

    public OpenAiChatProvider(HttpClient http, AiOptions options, ILogger<OpenAiChatProvider> logger)
        : base(http, options, logger)
    {
        _options = options;
    }

    public override bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    protected override string ResolveEndpoint()
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? "https://api.openai.com/v1"
            : _options.BaseUrl.TrimEnd('/');

        return $"{baseUrl}/chat/completions";
    }

    protected override object BuildRequestBody(string prompt, int maxOutputTokens) => new
    {
        model = _options.Model,
        // Low temperature: the facts are computed, so the model only needs to phrase
        // them clearly. Creative variation would only add noise.
        temperature = 0.2,
        max_tokens = maxOutputTokens,
        response_format = new { type = "json_object" },
        messages = new object[]
        {
            new { role = "system", content = SystemPrompt },
            new { role = "user", content = prompt },
        },
    };

    protected override string? ExtractText(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var first = choices[0];

        if (!first.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content))
        {
            return null;
        }

        return content.ValueKind == JsonValueKind.String ? content.GetString() : null;
    }

    private const string SystemPrompt =
        "You are Datara, a data quality analyst. You will receive computed dataset "
        + "statistics. Never invent a number: every figure you state must appear in the "
        + "context you were given. Return only a JSON object.";
}
