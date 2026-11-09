using System.Text.Json;
using Datara.Application.Analysis;

namespace Datara.Application.Ai;

/// <summary>
/// Extracts a structured answer from raw model text.
///
/// Models wrap JSON in prose or code fences often enough that a strict parse would
/// fail on a large share of otherwise good responses, so this pulls the first
/// balanced JSON object out of the text instead.
/// </summary>
internal static class ModelOutputParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ParsedModelOutput? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var candidate = ExtractJsonObject(text);
        if (candidate is null)
        {
            return null;
        }

        ModelAnswer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<ModelAnswer>(candidate, Options);
        }
        catch (JsonException)
        {
            return null;
        }

        if (answer is null)
        {
            return null;
        }

        return new ParsedModelOutput(
            NullIfBlank(answer.Summary),
            NullIfBlank(answer.Answer),
            [],
            MapInsights(answer.Suggestions));
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<AiInsight> MapInsights(List<ModelInsight>? source)
    {
        if (source is null)
        {
            return [];
        }

        return source
            .Where(i => !string.IsNullOrWhiteSpace(i.Title) || !string.IsNullOrWhiteSpace(i.Detail))
            .Select(i => new AiInsight
            {
                Title = NullIfBlank(i.Title) ?? "Suggestion",
                Detail = NullIfBlank(i.Detail) ?? string.Empty,
                Evidence = NullIfBlank(i.Evidence),
                Column = NullIfBlank(i.Column),
            })
            .Where(i => !string.IsNullOrWhiteSpace(i.Detail))
            .ToList();
    }

    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return text[start..(i + 1)];
                    }

                    break;
            }
        }

        return null;
    }

    private sealed class ModelAnswer
    {
        public string? Summary { get; init; }

        public string? Answer { get; init; }

        public List<ModelInsight>? Facts { get; init; }

        public List<ModelInsight>? Suggestions { get; init; }
    }

    private sealed class ModelInsight
    {
        public string? Title { get; init; }

        public string? Detail { get; init; }

        public string? Evidence { get; init; }

        public string? Column { get; init; }

        public string? Severity { get; init; }
    }

    public sealed record ParsedModelOutput(
        string? Summary,
        string? Answer,
        IReadOnlyList<AiInsight> Facts,
        IReadOnlyList<AiInsight> Suggestions);
}