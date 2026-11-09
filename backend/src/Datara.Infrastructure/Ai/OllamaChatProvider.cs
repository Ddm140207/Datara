using System.Text.Json;
using Datara.Application.Ai;
using Microsoft.Extensions.Logging;

namespace Datara.Infrastructure.Ai;

/// <summary>
/// Datara AI provider for a local Ollama instance. Ollama needs no API key, so
/// <see cref="IsConfigured"/> only requires the feature to be switched on, which
/// makes it the natural choice for a fully offline deployment.
/// </summary>
public sealed class OllamaChatProvider : HttpChatProvider
{
    private readonly AiOptions _options;

    public OllamaChatProvider(HttpClient http, AiOptions options, ILogger<OllamaChatProvider> logger)
        : base(http, options, logger)
    {
        _options = options;
    }

    public override bool IsConfigured => _options.Enabled;

    protected override string ResolveEndpoint()
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? "http://localhost:11434"
            : _options.BaseUrl.TrimEnd('/');

        return $"{baseUrl}/api/chat";
    }

    protected override object BuildRequestBody(string prompt, int maxOutputTokens) => new
    {
        model = _options.Model,
        stream = false,
        format = "json",
        options = new
        {
            temperature = 0.2,
            num_predict = maxOutputTokens,
        },
        messages = new object[]
        {
            new { role = "system", content = SystemPrompt },
            new { role = "user", content = prompt },
        },
    };

    protected override string? ExtractText(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return content.GetString();
    }

    private const string SystemPrompt =
        "You are Datara, a data quality analyst. You will receive computed dataset "
        + "statistics. Never invent a number: every figure you state must appear in the "
        + "context you were given. Return only a JSON object.";
}
