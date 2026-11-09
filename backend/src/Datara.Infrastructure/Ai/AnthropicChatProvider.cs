using System.Text.Json;
using Datara.Application.Ai;
using Microsoft.Extensions.Logging;

namespace Datara.Infrastructure.Ai;

/// <summary>Datara AI provider for the Anthropic Messages API.</summary>
public sealed class AnthropicChatProvider : HttpChatProvider
{
    private const string AnthropicVersion = "2023-06-01";

    private readonly AiOptions _options;

    public AnthropicChatProvider(HttpClient http, AiOptions options, ILogger<AnthropicChatProvider> logger)
        : base(http, options, logger)
    {
        _options = options;
    }

    public override bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    protected override string ResolveEndpoint()
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? "https://api.anthropic.com"
            : _options.BaseUrl.TrimEnd('/');

        return $"{baseUrl}/v1/messages";
    }

    protected override void ApplyAuthentication(System.Net.Http.HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Add("x-api-key", _options.ApiKey);
        }

        request.Headers.Add("anthropic-version", AnthropicVersion);
    }

    protected override object BuildRequestBody(string prompt, int maxOutputTokens) => new
    {
        model = _options.Model,
        max_tokens = maxOutputTokens,
        temperature = 0.2,
        system = SystemPrompt,
        messages = new object[]
        {
            new { role = "user", content = prompt },
        },
    };

    protected override string? ExtractText(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        // The API returns a content block array; concatenate the text blocks.
        var builder = new System.Text.StringBuilder();

        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "text"
                && block.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String)
            {
                builder.Append(text.GetString());
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private const string SystemPrompt =
        "You are Datara, a data quality analyst. You will receive computed dataset "
        + "statistics. Never invent a number: every figure you state must appear in the "
        + "context you were given. Return only a JSON object.";
}
