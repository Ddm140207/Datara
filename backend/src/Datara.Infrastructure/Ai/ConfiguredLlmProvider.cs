using Datara.Application.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Datara.Infrastructure.Ai;

/// <summary>
/// Provider selected at runtime from <c>Ai:Provider</c>. Every call is delegated to
/// the chosen implementation; the factory itself never performs HTTP.
/// </summary>
public sealed class ConfiguredLlmProvider : ILlmProvider
{
    private readonly AiOptions _options;
    private readonly Func<ILlmProvider> _resolve;
    private readonly ILogger<ConfiguredLlmProvider> _logger;

    public ConfiguredLlmProvider(
        IOptions<AiOptions> options,
        Func<ILlmProvider> resolve,
        ILogger<ConfiguredLlmProvider> logger)
    {
        _options = options.Value;
        _resolve = resolve;
        _logger = logger;
    }

    public bool IsConfigured
    {
        get
        {
            // A provider that is switched off is never treated as configured, so a
            // stale API key cannot silently enable spending.
            if (!_options.Enabled)
            {
                return false;
            }

            try
            {
                return _resolve().IsConfigured;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not resolve the configured AI provider");
                return false;
            }
        }
    }

    public async Task<string?> CompleteAsync(
        string prompt,
        int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        try
        {
            // The call has to be awaited inside the try. Wrapping only the task
            // creation would leave every faulted task (timeouts, malformed BaseUrl,
            // provider bugs) to surface as a failed request instead of a fallback.
            return await _resolve().CompleteAsync(prompt, maxOutputTokens, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                      || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "The AI provider failed; falling back to deterministic output");
            return null;
        }
    }
}
