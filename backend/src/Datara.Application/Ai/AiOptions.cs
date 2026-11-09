namespace Datara.Application.Ai;

/// <summary>Configuration for the LLM provider behind Datara AI.</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>
    /// When false, Datara AI answers from the calculated context alone using
    /// deterministic rules. This is the default so the product works with no API key.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Provider identifier. Supported: "openai", "anthropic", "ollama".</summary>
    public string Provider { get; set; } = "openai";

    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>API key. Supply through environment variables; never commit it.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Optional override for self-hosted and proxied providers.</summary>
    public string? BaseUrl { get; set; }

    public int MaxOutputTokens { get; set; } = 1200;

    /// <summary>Hard ceiling on the serialised context sent to the provider.</summary>
    public int MaxContextCharacters { get; set; } = 24_000;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Approximate USD budget guardrail, recorded per request.</summary>
    public decimal MaxCostPerRequestUsd { get; set; } = 0.05m;
}