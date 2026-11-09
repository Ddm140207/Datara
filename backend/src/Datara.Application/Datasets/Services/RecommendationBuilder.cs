using Datara.Application.Datasets.Dtos;

namespace Datara.Application.Datasets.Services;

/// <summary>
/// Turns detected issues into a prioritised, deduplicated action list.
///
/// The issue detector produces findings keyed by detection rule; this turns them
/// into instructions a data scientist can execute, ordered so the highest-impact
/// work appears first.
/// </summary>
internal static class RecommendationBuilder
{
    public static List<RecommendedActionDto> Build(
        IReadOnlyList<QualityIssueDto> issues,
        DatasetAnalysis analysis)
    {
        var actions = new List<RecommendedActionDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(RecommendedActionDto action)
        {
            if (seen.Add($"{action.Key}:{action.Column}"))
            {
                actions.Add(action);
            }
        }

        // Highest severity and score impact first, so ordering is deterministic.
        foreach (var issue in issues
                     .OrderByDescending(i => SeverityRank(i.Severity))
                     .ThenByDescending(i => i.ScoreImpact))
        {
            switch (issue.Category)
            {
                case nameof(Domain.Enums.IssueCategory.MissingValues)
                    when issue.Evidence.TryGetValue("missing_percentage", out var raw)
                         && raw is System.Text.Json.JsonElement element
                         && element.ValueKind == System.Text.Json.JsonValueKind.Number
                         && element.GetDouble() >= 20:
                    Add(new RecommendedActionDto
                    {
                        Key = "impute_column",
                        Title = $"Impute missing values in `{issue.Column}`",
                        Detail = $"{Percentage(element)} of rows are empty. Use median "
                                 + "imputation for numeric columns, or an \"Unknown\" bucket "
                                 + "for categorical ones, and add a missingness indicator.",
                        Severity = issue.Severity,
                        Column = issue.Column,
                        Category = "Missing data",
                    });
                    break;

                case nameof(Domain.Enums.IssueCategory.ConstantColumn):
                    Add(new RecommendedActionDto
                    {
                        Key = "drop_constant_column",
                        Title = $"Drop the constant column `{issue.Column}`",
                        Detail = "It holds a single value, so it carries no information and "
                                 + "will be dropped or ignored by most estimators anyway.",
                        Severity = issue.Severity,
                        Column = issue.Column,
                        Category = "Schema",
                    });
                    break;

                case nameof(Domain.Enums.IssueCategory.Duplicates):
                    Add(new RecommendedActionDto
                    {
                        Key = "deduplicate_rows",
                        Title = "De-duplicate rows",
                        Detail = issue.Recommendation,
                        Severity = issue.Severity,
                        Column = issue.Column,
                        Category = "Duplicates",
                    });
                    break;

                case nameof(Domain.Enums.IssueCategory.DuplicateColumn):
                    Add(new RecommendedActionDto
                    {
                        Key = "drop_duplicate_column",
                        Title = $"Resolve duplicated column `{issue.Column}`",
                        Detail = issue.Recommendation,
                        Severity = issue.Severity,
                        Column = issue.Column,
                        Category = "Schema",
                    });
                    break;

                case nameof(Domain.Enums.IssueCategory.Outliers):
                    Add(new RecommendedActionDto
                    {
                        Key = "review_outliers",
                        Title = $"Review outliers in `{issue.Column}`",
                        Detail = issue.Recommendation,
                        Severity = issue.Severity,
                        Column = issue.Column,
                        Category = "Outliers",
                    });
                    break;

                case nameof(Domain.Enums.IssueCategory.Identifiers):
                    Add(new RecommendedActionDto
                    {
                        Key = "exclude_identifier",
                        Title = $"Exclude `{issue.Column}` from the feature set",
                        Detail = issue.Recommendation,
                        Severity = issue.Severity,
                        Column = issue.Column,
                        Category = "Features",
                    });
                    break;

                case nameof(Domain.Enums.IssueCategory.Cardinality):
                    Add(new RecommendedActionDto
                    {
                        Key = "reduce_cardinality",
                        Title = $"Reduce the cardinality of `{issue.Column}`",
                        Detail = issue.Recommendation,
                        Severity = issue.Severity,
                        Column = issue.Column,
                        Category = "Features",
                    });
                    break;

                case nameof(Domain.Enums.IssueCategory.ClassImbalance):
                    Add(new RecommendedActionDto
                    {
                        Key = "handle_class_imbalance",
                        Title = "Address class imbalance",
                        Detail = issue.Recommendation,
                        Severity = issue.Severity,
                        Column = issue.Column,
                        Category = "Modelling",
                    });
                    break;

                case nameof(Domain.Enums.IssueCategory.Consistency):
                    Add(new RecommendedActionDto
                    {
                        Key = "normalise_values",
                        Title = $"Normalise values in `{issue.Column}`",
                        Detail = issue.Recommendation,
                        Severity = issue.Severity,
                        Column = issue.Column,
                        Category = "Cleaning",
                    });
                    break;

                case nameof(Domain.Enums.IssueCategory.SmallSample):
                    Add(new RecommendedActionDto
                    {
                        Key = "use_cross_validation",
                        Title = "Use cross-validation instead of a single split",
                        Detail = issue.Recommendation,
                        Severity = issue.Severity,
                        Column = issue.Column,
                        Category = "Modelling",
                    });
                    break;

                case nameof(Domain.Enums.IssueCategory.HighDimensionality):
                    Add(new RecommendedActionDto
                    {
                        Key = "reduce_dimension",
                        Title = "Reduce dimensionality",
                        Detail = issue.Recommendation,
                        Severity = issue.Severity,
                        Column = issue.Column,
                        Category = "Modelling",
                    });
                    break;
            }
        }

        // Leakage is a gating concern: it must lead the list even when no issue row
        // exists for it, because the analysis service can raise it independently.
        foreach (var signal in analysis.LeakageSignals.OrderByDescending(s => s.Severity))
        {
            if (signal.Severity is not ("High" or "Critical"))
            {
                continue;
            }

            Add(new RecommendedActionDto
            {
                Key = "remove_leakage_source",
                Title = $"Remove or exclude `{signal.Column}` before training",
                Detail = signal.Rationale,
                Severity = signal.Severity,
                Column = signal.Column,
                Category = "Target leakage",
            });
        }

        return actions;
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "Critical" => 5,
        "High" => 4,
        "Medium" => 3,
        "Low" => 2,
        _ => 1,
    };

    private static string Percentage(System.Text.Json.JsonElement element) =>
        $"{element.GetDouble():0.0}%";
}