using Datara.Application.Ai;
using Datara.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Datara.UnitTests.Ai;

/// <summary>
/// <see cref="ConfiguredLlmProvider"/> is the composition boundary that makes Datara AI
/// safe to put in front of a request: it is the last line of defence before an LLM
/// failure becomes a failed API call, and it is also what stops a disabled provider
/// from being used because a key happens to be present in configuration.
/// </summary>
public sealed class ConfiguredLlmProviderTests
{
    [Fact]
    public async Task A_disabled_provider_is_never_called_even_with_a_valid_key()
    {
        var inner = new RecordingProvider { IsConfigured = true };
        var provider = Build(new AiOptions { Enabled = false, ApiKey = "sk-present" }, () => inner);

        var result = await provider.CompleteAsync("prompt", 100, default);

        Assert.Null(result);
        Assert.Equal(0, inner.Calls);
        Assert.False(provider.IsConfigured);
    }

    [Fact]
    public async Task An_exception_from_the_provider_becomes_a_null_result()
    {
        var inner = new RecordingProvider
        {
            IsConfigured = true,
            Failure = new HttpRequestException("connection refused"),
        };
        var provider = Build(new AiOptions { Enabled = true }, () => inner);

        var result = await provider.CompleteAsync("prompt", 100, default);

        Assert.Null(result);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task A_timeout_becomes_a_null_result_rather_than_propagating()
    {
        var inner = new RecordingProvider
        {
            IsConfigured = true,
            Failure = new TaskCanceledException("timeout"),
        };
        var provider = Build(new AiOptions { Enabled = true }, () => inner);

        Assert.Null(await provider.CompleteAsync("prompt", 100, default));
    }

    [Fact]
    public async Task An_enabled_provider_result_is_passed_through_untouched()
    {
        var inner = new RecordingProvider { IsConfigured = true, Result = "raw model text" };
        var provider = Build(new AiOptions { Enabled = true }, () => inner);

        var result = await provider.CompleteAsync("prompt", 100, default);

        Assert.Equal("raw model text", result);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Output_token_budget_is_forwarded_to_the_provider()
    {
        var inner = new RecordingProvider { IsConfigured = true, Result = "ok" };
        var provider = Build(new AiOptions { Enabled = true, MaxOutputTokens = 321 }, () => inner);

        await provider.CompleteAsync("prompt", 321, default);

        Assert.Equal(321, inner.LastMaxOutputTokens);
    }

    [Fact]
    public void A_provider_that_cannot_be_resolved_is_reported_as_unconfigured()
    {
        var provider = Build(
            new AiOptions { Enabled = true },
            () => throw new InvalidOperationException("no such provider registered"));

        Assert.False(provider.IsConfigured);
    }

    [Fact]
    public async Task An_unresolvable_provider_also_yields_a_null_completion()
    {
        var provider = Build(
            new AiOptions { Enabled = true },
            () => throw new InvalidOperationException("no such provider registered"));

        Assert.Null(await provider.CompleteAsync("prompt", 100, default));
    }

    [Fact]
    public void A_provider_missing_its_key_is_reported_as_unconfigured()
    {
        var provider = Build(new AiOptions { Enabled = true }, () => new RecordingProvider());

        Assert.False(provider.IsConfigured);
    }

    private static ConfiguredLlmProvider Build(AiOptions options, Func<ILlmProvider> resolve) =>
        new(Options.Create(options), resolve, NullLogger<ConfiguredLlmProvider>.Instance);

    private sealed class RecordingProvider : ILlmProvider
    {
        public bool IsConfigured { get; init; }

        public string? Result { get; init; }

        public Exception? Failure { get; init; }

        public int Calls { get; private set; }

        public int LastMaxOutputTokens { get; private set; }

        public Task<string?> CompleteAsync(
            string prompt,
            int maxOutputTokens,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastMaxOutputTokens = maxOutputTokens;

            return Failure is not null
                ? Task.FromException<string?>(Failure)
                : Task.FromResult(Result);
        }
    }
}
