using Datara.Application.Analysis;
using Datara.Application.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Datara.UnitTests.Ai;

/// <summary>
/// The context builder is the security boundary for Datara AI: it decides exactly what
/// leaves the process. These tests pin that boundary rather than the wording.
/// </summary>
public sealed class DatasetContextBuilderTests
{
    private static DatasetContextBuilder Build(int maxCharacters = 24_000) =>
        new(
            Options.Create(new AiOptions { MaxContextCharacters = maxCharacters }),
            NullLogger<DatasetContextBuilder>.Instance);

    [Fact]
    public void Context_is_capped_by_configuration()
    {
        var columns = Enumerable.Range(0, 500)
            .Select(i => new ColumnProfileResponse { Name = $"col{i}", InferredKind = "numeric" })
            .ToList();

        var context = Build(maxCharacters: 2_000).BuildContext(Profile(columns), "wide");

        Assert.True(context.Length <= 2_000, $"context was {context.Length} characters");
    }

    [Fact]
    public void Only_a_bounded_number_of_columns_reach_the_model()
    {
        var columns = Enumerable.Range(0, 300)
            .Select(i => new ColumnProfileResponse { Name = $"col{i}", InferredKind = "numeric" })
            .ToList();

        var context = Build().BuildContext(Profile(columns), "wide");

        // A 300-column dataset must not serialise 300 column blocks.
        Assert.DoesNotContain("col299", context);
        Assert.Contains("further columns omitted", context);
    }

    [Fact]
    public void Only_a_bounded_number_of_categories_per_column_reach_the_model()
    {
        var column = new ColumnProfileResponse
        {
            Name = "plan",
            InferredKind = "categorical",
            TopCategories = Enumerable.Range(0, 200)
                .Select(i => new CategoryFrequencyResponse { Value = $"v{i}", Count = i })
                .ToList(),
        };

        var context = Build().BuildContext(Profile([column]), "cats");

        Assert.Contains("v5=", context);
        Assert.DoesNotContain("v6=", context);
    }

    [Fact]
    public void Category_values_are_truncated_so_a_cell_cannot_flood_the_prompt()
    {
        var column = new ColumnProfileResponse
        {
            Name = "free_text",
            InferredKind = "text",
            TopCategories =
            [
                new CategoryFrequencyResponse { Value = new string('x', 5_000), Count = 1 },
            ],
        };

        var context = Build().BuildContext(Profile([column]), "flood");

        Assert.True(context.Length < 5_000);
    }

    [Fact]
    public void Raw_rows_are_included_only_as_the_capped_deterministic_sample()
    {
        var profile = Profile([]);
        profile = new DatasetProfileResponse
        {
            RowCount = 1_000_000,
            Columns = profile.Columns,
            Samples = new SampleRowsResponse
            {
                Columns = ["a", "b"],
                Rows = Enumerable.Range(0, 500).Select(i => (List<string>)[$"r{i}a", $"r{i}b"]).ToList(),
            },
        };

        var context = Build().BuildContext(profile, "rows");

        // The sample is bounded, so a million-row dataset still yields a small prompt.
        Assert.Contains("r0a", context);
        Assert.DoesNotContain("r499a", context);
    }

    [Fact]
    public void Dataset_name_is_included_so_answers_can_reference_it()
    {
        var context = Build().BuildContext(Profile([]), "quarterly_churn");

        Assert.Contains("quarterly_churn", context);
    }

    [Fact]
    public void Warnings_and_parse_errors_are_surfaced_but_bounded()
    {
        var profile = Profile([]);
        profile = new DatasetProfileResponse
        {
            Columns = profile.Columns,
            Warnings = Enumerable.Range(0, 500).Select(i => $"warning {i}").ToList(),
            ParseErrors = Enumerable.Range(0, 500).Select(i => $"error {i}").ToList(),
        };

        var context = Build().BuildContext(profile, "noisy");

        Assert.Contains("warning 0", context);
        Assert.DoesNotContain("warning 20", context);
        Assert.DoesNotContain("error 20", context);
    }

    [Fact]
    public void A_profile_with_no_readiness_still_produces_a_usable_context()
    {
        var profile = new DatasetProfileResponse { RowCount = 10, ColumnCount = 1 };

        var context = Build().BuildContext(profile, "sparse");

        Assert.Contains("rows: 10", context);
        Assert.Contains("sparse", context);
    }

    [Fact]
    public void Null_numeric_statistics_are_rendered_without_throwing()
    {
        var column = new ColumnProfileResponse
        {
            Name = "sparse_numeric",
            InferredKind = "numeric",
            Numeric = new NumericSummaryResponse { Mean = 1.0, Median = null, Std = null },
            Outliers = [new OutlierSummaryResponse { Method = "iqr", LowerBound = null, UpperBound = null }],
        };

        var context = Build().BuildContext(Profile([column]), "nulls");

        Assert.Contains("sparse_numeric", context);
        Assert.Contains("outliers[iqr]", context);
    }

    private static DatasetProfileResponse Profile(List<ColumnProfileResponse> columns) => new()
    {
        RowCount = 100,
        ColumnCount = columns.Count,
        Columns = columns,
    };
}
