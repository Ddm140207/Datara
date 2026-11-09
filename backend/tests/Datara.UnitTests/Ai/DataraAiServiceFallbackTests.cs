using System.Text.Json;
using Datara.Application.Analysis;
using Datara.Application.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Datara.UnitTests.Ai;

/// <summary>
/// Datara AI must keep working - and keep its guarantees - when no provider is
/// configured or when the provider fails. These tests use a provider that always
/// throws or returns junk, which is the realistic failure mode.
/// </summary>
public sealed class DataraAiServiceFallbackTests
{
    private static readonly DatasetProfileResponse Profile = new()
    {
        RowCount = 1_000,
        ColumnCount = 3,
        TotalCells = 3_000,
        TotalMissing = 300,
        OverallMissingPercentage = 10m,
        Columns =
        [
            new ColumnProfileResponse
            {
                Name = "age",
                InferredKind = "numeric",
                NullCount = 30,
                MissingPercentage = 3m,
                UniqueCount = 60,
                Numeric = new NumericSummaryResponse { Mean = 40, Min = 18, Max = 70 },
            },
            new ColumnProfileResponse
            {
                Name = "plan",
                InferredKind = "categorical",
                TopCategories = [new CategoryFrequencyResponse { Value = "pro", Count = 700, Percentage = 70m }],
            },
            new ColumnProfileResponse
            {
                Name = "constant",
                InferredKind = "categorical",
                IsConstant = true,
            },
        ],
        QualityIssues =
        [
            new QualityIssueResponse
            {
                Code = "high_missingness",
                Severity = "high",
                Category = "completeness",
                Column = "age",
                Title = "High missingness in age",
                Description = "3% missing.",
                Recommendation = "Impute or drop.",
            },
            new QualityIssueResponse
            {
                Code = "constant_column",
                Severity = "medium",
                Category = "usability",
                Column = "constant",
                Title = "Constant column",
                Description = "Single value.",
                Recommendation = "Remove.",
            },
        ],
        MlReadiness = new MlReadinessResponse
        {
            Overall = 62.5m,
            Grade = "C",
            Verdict = "Usable after cleanup.",
            DataQualityScore = 64m,
            Components =
            [
                new ScoreComponentResponse
                {
                    Key = "completeness",
                    Label = "Completeness",
                    Score = 88m,
                    Weight = 0.22m,
                    Rationale = "10% of cells missing.",
                },
            ],
            Strengths = ["Low duplicate rate"],
            Risks = ["Constant column present"],
        },
    };

    [Fact]
    public async Task Summary_works_with_no_provider_configured()
    {
        var service = Service(new UnconfiguredProvider());

        var summary = await service.GenerateSummaryAsync(Profile, "customers", default);

        Assert.False(summary.GeneratedByModel);
        Assert.Equal("deterministic", summary.Provider);
        Assert.Contains("1,000 rows", summary.Summary);
        Assert.NotEmpty(summary.Insights);
        Assert.NotEmpty(summary.Recommendations);
    }

    [Fact]
    public async Task An_unusable_provider_response_falls_back_instead_of_throwing()
    {
        var service = Service(new NullProvider());

        var summary = await service.GenerateSummaryAsync(Profile, "customers", default);

        Assert.False(summary.GeneratedByModel);
        Assert.Contains("1,000 rows", summary.Summary);
        Assert.Contains("did not return a usable response", summary.Notice);
    }

    [Fact]
    public async Task Unparseable_provider_output_falls_back_instead_of_throwing()
    {
        var service = Service(new JunkProvider());

        var summary = await service.GenerateSummaryAsync(Profile, "customers", default);

        Assert.False(summary.GeneratedByModel);
        Assert.NotEmpty(summary.Insights);
    }

    [Fact]
    public async Task A_configured_provider_is_used_and_flagged()
    {
        var service = Service(new EchoProvider());

        var summary = await service.GenerateSummaryAsync(Profile, "customers", default);

        Assert.True(summary.GeneratedByModel);
        Assert.Equal("openai", summary.Provider);
        Assert.Equal("test-model", summary.Model);
        Assert.Equal("model summary", summary.Summary);
    }

    [Fact]
    public async Task Facts_are_always_recomputed_from_the_profile_even_when_a_model_answers()
    {
        // The provider claims a row count that appears in no computed statistic.
        var service = Service(new InventingProvider());

        var response = await service.AskAsync(
            Profile,
            "customers",
            new AiChatRequest { Message = "How many rows are there?" },
            default);

        Assert.True(response.GeneratedByModel);

        // Narrative comes from the model and is therefore unverified by design...
        Assert.Contains("4,321", response.Answer);

        // ...but the facts list must be calculated, so the invented figure is absent
        // from it and every fact carries traceable evidence.
        Assert.DoesNotContain(response.Facts, f => f.Detail.Contains("4,321")
                                                 || f.Evidence?.Contains("4,321") == true);
        Assert.All(response.Facts, f =>
        {
            Assert.Equal("fact", f.Kind);
            Assert.False(string.IsNullOrWhiteSpace(f.Evidence));
        });
        Assert.NotEmpty(response.Facts);
    }

    [Fact]
    public async Task Chat_answers_without_a_provider()
    {
        var service = Service(new UnconfiguredProvider());

        var response = await service.AskAsync(
            Profile,
            "customers",
            new AiChatRequest { Message = "What are the main data quality issues?" },
            default);

        Assert.NotEmpty(response.Answer);
        Assert.False(response.GeneratedByModel);
        Assert.Equal("deterministic", response.Provider);
    }

    [Fact]
    public async Task Chat_routes_a_quality_question_to_the_detected_issues()
    {
        var service = Service(new UnconfiguredProvider());

        var response = await service.AskAsync(
            Profile,
            "customers",
            new AiChatRequest { Message = "What are the main data quality issues?" },
            default);

        Assert.Contains(response.Facts, f => f.Evidence == "high_missingness");
    }

    [Fact]
    public async Task Chat_routes_an_ml_question_to_the_readiness_breakdown()
    {
        var service = Service(new UnconfiguredProvider());

        var response = await service.AskAsync(
            Profile,
            "customers",
            new AiChatRequest { Message = "Is this dataset ready for machine learning?" },
            default);

        var facts = string.Join(" | ", response.Facts.Select(f => $"{f.Title} {f.Evidence}"));

        // The readiness score, its grade and the weakest component, not the generic
        // dataset-shape facts a generic question would return.
        Assert.Contains("63/100", facts);
        Assert.Contains("grade C", facts);
        Assert.Contains("completeness", facts);
        Assert.DoesNotContain("Duplicate rows", facts);
    }

    [Fact]
    public async Task A_blank_question_is_rejected()
    {
        var service = Service(new UnconfiguredProvider());

        await Assert.ThrowsAsync<ArgumentException>(() => service.AskAsync(
            Profile,
            "customers",
            new AiChatRequest { Message = "   " },
            default));
    }

    [Fact]
    public async Task Summarising_an_unanalysed_dataset_is_rejected()
    {
        var service = Service(new UnconfiguredProvider());
        var unanalysed = new DatasetProfileResponse { RowCount = 5, ColumnCount = 1 };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateSummaryAsync(unanalysed, "raw", default));
    }

    [Fact]
    public void Capabilities_report_the_deterministic_path_when_disabled()
    {
        var capabilities = Service(new UnconfiguredProvider()).GetCapabilities();

        Assert.False(capabilities.Enabled);
        Assert.True(capabilities.FallbackAvailable);
        Assert.Equal("deterministic-rules", capabilities.Model);
    }

    [Fact]
    public void Capabilities_never_expose_the_api_key()
    {
        var capabilities = Service(new UnconfiguredProvider()).GetCapabilities();
        var serialised = JsonSerializer.Serialize(capabilities);

        Assert.DoesNotContain("sk-test-secret", serialised);
    }

    private static DataraAiService Service(ILlmProvider provider) => new(
        new DatasetContextBuilder(
            Options.Create(new AiOptions()),
            NullLogger<DatasetContextBuilder>.Instance),
        provider,
        new DeterministicAiFallback(),
        new AiOptions
        {
            Enabled = provider.IsConfigured,
            Model = "test-model",
            ApiKey = "sk-test-secret",
        },
        NullLogger<DataraAiService>.Instance);

    private sealed class UnconfiguredProvider : ILlmProvider
    {
        public bool IsConfigured => false;

        public Task<string?> CompleteAsync(string prompt, int maxOutputTokens, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NullProvider : ILlmProvider
    {
        public bool IsConfigured => true;

        public Task<string?> CompleteAsync(string prompt, int maxOutputTokens, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private sealed class JunkProvider : ILlmProvider
    {
        public bool IsConfigured => true;

        public Task<string?> CompleteAsync(string prompt, int maxOutputTokens, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("I'm sorry, I can't help with that.");
    }

    private sealed class EchoProvider : ILlmProvider
    {
        public bool IsConfigured => true;

        public Task<string?> CompleteAsync(string prompt, int maxOutputTokens, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("""{"summary":"model summary","answer":"ok","suggestions":[]}""");
    }

    private sealed class InventingProvider : ILlmProvider
    {
        public bool IsConfigured => true;

        public Task<string?> CompleteAsync(string prompt, int maxOutputTokens, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(
                """
                {"answer":"There are 4,321 rows and the average age is 999.","suggestions":[]}
                """);
    }
}
