using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Datara.Application.Ai;
using Microsoft.Extensions.Logging;

namespace Datara.Infrastructure.Ai;

/// <summary>Shared plumbing for chat-completions style providers.</summary>
public abstract class HttpChatProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly AiOptions _options;
    private readonly ILogger _logger;

    protected HttpChatProvider(HttpClient http, AiOptions options, ILogger logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    /// <summary>True only when the provider has everything it needs to answer.</summary>
    public abstract bool IsConfigured { get; }

    protected abstract string ResolveEndpoint();

    /// <summary>Serialises the provider-specific request body for a single user prompt.</summary>
    protected abstract object BuildRequestBody(string prompt, int maxOutputTokens);

    /// <summary>Pulls the assistant text out of a provider-specific response body.</summary>
    protected abstract string? ExtractText(string json);

    public async Task<string?> CompleteAsync(
        string prompt,
        int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, ResolveEndpoint())
        {
            Content = new StringContent(
                JsonSerializer.Serialize(BuildRequestBody(prompt, maxOutputTokens)),
                Encoding.UTF8,
                "application/json"),
        };

        ApplyAuthentication(request);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);

        try
        {
            using var response = await _http.SendAsync(request, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                // Never throw for a model error: the caller falls back to the
                // deterministic path, which is a better experience than a 500.
                _logger.LogWarning(
                    "{Provider} returned {StatusCode} for an AI request",
                    _options.Provider,
                    response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var text = ExtractText(json);

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("{Provider} returned an empty completion", _options.Provider);
            }

            return text;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("{Provider} AI request timed out", _options.Provider);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "{Provider} AI request failed", _options.Provider);
            return null;
        }
    }

    protected virtual void ApplyAuthentication(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }
}
