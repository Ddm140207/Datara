using System.Globalization;
using Datara.Application.Analysis;
using Datara.Application.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Datara.UnitTests.Ai;

/// <summary>
/// Datara AI renders figures into English product copy and into the context a model
/// reads. Both must stay invariant: on a machine whose locale groups digits with a dot
/// ("1.000") or separates decimals with a comma ("0,22"), the numbers either read as
/// wrong English or, worse, change meaning inside a prompt.
/// </summary>
public sealed class AiNarrativeFormattingTests
{
    private static readonly DatasetProfileResponse Profile = new()
    {
        RowCount = 1_234_567,
        ColumnCount = 9,
        TotalCells = 11_111_103,
        TotalMissing = 1_000_000,
        OverallMissingPercentage = 9.0004m,
        DuplicateRowCount = 2_500,
        DuplicatePercentage = 0.2025m,
        MemoryUsageBytes = 1_573_724,
        Columns =
        [
            new ColumnProfileResponse
            {
                Name = "score",
                InferredKind = "numeric",
                NullCount = 12_345,
                MissingPercentage = 1.0005m,
                UniqueCount = 987_654,
                Numeric = new NumericSummaryResponse { Mean = 0.5, Std = 1234.56789 },
            },
        ],
        MlReadiness = new MlReadinessResponse
        {
            Overall = 62.5m,
            Grade = "C",
            Verdict = "Usable after cleanup.",
            DataQualityScore = 64.25m,
            Components =
            [
                new ScoreComponentResponse
                {
                    Key = "completeness",
                    Label = "Completeness",
                    Score = 88.5m,
                    Weight = 0.22m,
                    Rationale = "9% of cells are missing.",
                },
            ],
        },
    };

    [Theory]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    [InlineData("ar-EG")]
    public void Narrative_numbers_do_not_depend_on_the_machine_locale(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var summary = new DeterministicAiFallback().BuildSummary(Profile, "customers");

            Assert.Contains("1,234,567 rows", summary.Summary);
            Assert.Contains("9.0%", summary.Summary);
            Assert.Contains("63/100", summary.Summary);

            var facts = string.Join(" ", summary.Insights.Select(i => i.Detail));
            Assert.Contains("11,111,103 cells", facts);
            Assert.Contains("1.5 MB", facts);

            var ml = summary.Insights.Single(i => i.Title == "ML readiness");
            Assert.Contains("64/100", ml.Evidence);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("es-ES")]
    [InlineData("de-DE")]
    public void Model_context_uses_dot_decimals_regardless_of_locale(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var context = new DatasetContextBuilder(
                Options.Create(new AiOptions()),
                NullLogger<DatasetContextBuilder>.Instance).BuildContext(Profile, "customers");

            Assert.Contains("mean=0.5", context);
            Assert.DoesNotContain("mean=0,5", context);
            Assert.Contains("std=1234.5679", context);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
