namespace Datara.Application.Ai;

/// <summary>
/// One statement in an AI response, explicitly labelled as fact or suggestion so
/// the UI can render calculated facts differently from model advice.
/// </summary>
public sealed record AiInsight
{
    /// <summary>Short label used as a heading, for example "Missing values in `income`".</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The explanation itself.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>"fact" when traceable to a computed value, "suggestion" when advice.</summary>
    public string Kind { get; init; } = "suggestion";

    /// <summary>The statistic this claim rests on, when it is a fact.</summary>
    public string? Evidence { get; init; }

    public string? Column { get; init; }

    public string? Severity { get; init; }
}

/// <summary>Structured dataset summary produced by Datara AI.</summary>
public sealed record AiDatasetSummary
{
    public Guid DatasetId { get; init; }

    public string DatasetName { get; init; } = string.Empty;

    /// <summary>Two or three sentence narrative summary.</summary>
    public string Summary { get; init; } = string.Empty;

    public List<AiInsight> Insights { get; init; } = new();

    public List<AiInsight> Recommendations { get; init; } = new();

    /// <summary>True when an LLM generated this; false when it was rule derived.</summary>
    public bool GeneratedByModel { get; init; }

    public string Provider { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? Notice { get; init; }
}

public sealed record AiChatRequest
{
    public string Message { get; init; } = string.Empty;

    /// <summary>Prior turns, oldest first, for conversational continuity.</summary>
    public List<AiChatTurn> History { get; init; } = new();

    public string? TargetColumn { get; init; }
}

public sealed record AiChatTurn
{
    public string Role { get; init; } = "user";

    public string Content { get; init; } = string.Empty;
}

public sealed record AiChatResponse
{
    public Guid DatasetId { get; init; }

    public string Answer { get; init; } = string.Empty;

    /// <summary>Claims extracted from the calculated context, not the model.</summary>
    public List<AiInsight> Facts { get; init; } = new();

    /// <summary>Model advice, clearly separated from calculated facts.</summary>
    public List<AiInsight> Suggestions { get; init; } = new();

    public bool GeneratedByModel { get; init; }

    public string Provider { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? Notice { get; init; }
}

/// <summary>Describes what the configured AI provider can currently do.</summary>
public sealed record AiCapabilitiesDto
{
    public bool Enabled { get; init; }

    public string Provider { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    /// <summary>True when a deterministic fallback can answer without an LLM.</summary>
    public bool FallbackAvailable { get; init; }

    public string Notice { get; init; } = string.Empty;
}