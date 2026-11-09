using System.Globalization;
using Datara.Application.Analysis;
using Datara.Application.Ai.Abstractions;
using Datara.Domain.Entities;
using Datara.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Datara.Application.Ai;

/// <summary>
/// Assembles the structured dataset context the model is allowed to see.
///
/// Design rule: the LLM never receives raw rows beyond a small, deterministic
/// sample, and never receives the file itself. Everything here is either computed
/// by the analysis service or curated by this layer, which keeps prompts small,
/// auditable and free of injection surface from the uploaded data.
/// </summary>
public sealed class DatasetContextBuilder : IDatasetContextBuilder
{
    private const int MaxColumnsInContext = 40;
    private const int MaxIssuesInContext = 25;
    private const int MaxCategoriesInContext = 6;
    private const int MaxCorrelationPairsInContext = 20;
    private const int MaxSampleRowsInContext = 8;
    private const int MaxSampleColumnWidth = 24;

    /// <summary>
    /// Category values are attacker-controlled cell contents, so they are clipped to
    /// the same order of magnitude as sample cells. Without this a single pathological
    /// value (a base64 blob, a nested JSON payload) would consume the whole budget and
    /// push every other statistic out of the context.
    /// </summary>
    private const int MaxCategoryValueLength = 40;

    private const string TruncationNotice =
        "\n\n[context truncated at the configured character limit]";

    private readonly ILogger<DatasetContextBuilder> _logger;
    private readonly AiOptions _options;

    public DatasetContextBuilder(IOptions<AiOptions> options, ILogger<DatasetContextBuilder> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string BuildContext(DatasetProfileResponse profile, string datasetName) =>
        Build(profile, datasetName);

    public string BuildReadableContext(DatasetProfileResponse profile, string datasetName) =>
        Build(profile, datasetName);

    private string Build(DatasetProfileResponse profile, string datasetName)
    {
        var builder = new System.Text.StringBuilder();

        builder.AppendLine("DATASET METADATA");
        builder.AppendLine($"  name: {datasetName}");
        builder.AppendLine($"  rows: {profile.RowCount}");
        builder.AppendLine($"  columns: {profile.ColumnCount}");
        builder.AppendLine($"  total_cells: {profile.TotalCells}");
        builder.AppendLine($"  missing_cells: {profile.TotalMissing} ({profile.OverallMissingPercentage}%)");
        builder.AppendLine($"  duplicate_rows: {profile.DuplicateRowCount} ({profile.DuplicatePercentage}%)");
        builder.AppendLine($"  memory_bytes: {profile.MemoryUsageBytes}");
        if (profile.FullyEmptyColumns.Count > 0)
        {
            builder.AppendLine(
                $"  fully_empty_columns: [{string.Join(", ", profile.FullyEmptyColumns.Take(20))}]");
        }

        if (profile.MlReadiness is { } readiness)
        {
            builder.AppendLine();
            builder.AppendLine("ML READINESS (calculated)");
            builder.AppendLine($"  overall: {readiness.Overall} (grade {readiness.Grade})");
            builder.AppendLine($"  data_quality_score: {readiness.DataQualityScore}");
            builder.AppendLine($"  verdict: {readiness.Verdict}");
            foreach (var component in readiness.Components)
            {
                builder.AppendLine(
                    $"  - {component.Key}: {component.Score} (weight {component.Weight}) :: {component.Rationale}");
                foreach (var penalty in component.Penalties.Take(4))
                {
                    builder.AppendLine($"      penalty -{penalty.Points} [{penalty.Reason}] {penalty.Detail}");
                }
            }

            if (readiness.Gates.Count > 0)
            {
                builder.AppendLine("  gates:");
                foreach (var gate in readiness.Gates)
                {
                    builder.AppendLine($"      - {gate}");
                }
            }

            if (readiness.LeakageSignals.Count > 0)
            {
                builder.AppendLine("  target_leakage_signals:");
                foreach (var signal in readiness.LeakageSignals.Take(10))
                {
                    builder.AppendLine(
                        $"      - [{signal.Severity}] {signal.Column} ({signal.Signal}): {signal.Rationale}");
                }
            }

            if (readiness.ClassBalance is { } balance)
            {
                builder.AppendLine(
                    $"  class_balance: column={balance.Column} classes={balance.DistinctClasses} " +
                    $"minority={balance.MinorityPercentage}% majority={balance.MajorityPercentage}% " +
                    $"imbalanced={balance.IsImbalanced}");
            }
        }

        builder.AppendLine();
        builder.AppendLine($"COLUMNS ({profile.Columns.Count} total)");
        foreach (var column in profile.Columns.Take(MaxColumnsInContext))
        {
            builder.AppendLine($"  - {column.Name} [{column.InferredKind}] dtype={column.PandasDtype}");
            builder.AppendLine(
                $"      nulls={column.NullCount} ({column.MissingPercentage}%) unique={column.UniqueCount} " +
                $"cardinality_ratio={column.CardinalityRatio} constant={column.IsConstant}");

            if (column.Numeric is { } numeric)
            {
                builder.AppendLine(
                    $"      mean={R(numeric.Mean)} median={R(numeric.Median)} std={R(numeric.Std)} " +
                    $"min={R(numeric.Min)} max={R(numeric.Max)}");
                builder.AppendLine(
                    $"      p05={R(numeric.P05)} p25={R(numeric.P25)} p75={R(numeric.P75)} p95={R(numeric.P95)} " +
                    $"skew={R(numeric.Skewness)}");
            }

            if (column.TopCategories.Count > 0)
            {
                var top = column.TopCategories
                    .Take(MaxCategoriesInContext)
                    .Select(c => $"{Truncate(c.Value, MaxCategoryValueLength)}={c.Count} ({c.Percentage}%)");
                builder.AppendLine($"      top_categories: {string.Join(", ", top)}");
            }

            foreach (var outlier in column.Outliers.Take(2))
            {
                builder.AppendLine(
                    $"      outliers[{outlier.Method}]: count={outlier.Count} " +
                    $"({outlier.Percentage}%) bounds=[{R(outlier.LowerBound)}, {R(outlier.UpperBound)}]");
            }

            if (column.Notes.Count > 0)
            {
                builder.AppendLine($"      notes: {string.Join(" | ", column.Notes.Take(3))}");
            }
        }

        if (profile.Columns.Count > MaxColumnsInContext)
        {
            builder.AppendLine($"  ... {profile.Columns.Count - MaxColumnsInContext} further columns omitted");
        }

        if (profile.QualityIssues.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"DETECTED QUALITY ISSUES ({profile.QualityIssues.Count} total)");
            foreach (var issue in profile.QualityIssues.Take(MaxIssuesInContext))
            {
                builder.AppendLine(
                    $"  - [{issue.Severity}] {issue.Code} (column={issue.Column ?? "dataset"})");
                builder.AppendLine($"      {issue.Description}");
                builder.AppendLine($"      recommendation: {issue.Recommendation}");
            }
            if (profile.QualityIssues.Count > MaxIssuesInContext)
            {
                builder.AppendLine(
                    $"  ... {profile.QualityIssues.Count - MaxIssuesInContext} further issues omitted");
            }
        }

        if (profile.Correlations is { Pairs.Count: > 0 } correlations)
        {
            builder.AppendLine();
            builder.AppendLine("STRONGEST CORRELATIONS (pearson)");
            foreach (var pair in correlations.Pairs.Take(MaxCorrelationPairsInContext))
            {
                builder.AppendLine($"  - {pair.ColumnA} ~ {pair.ColumnB}: {pair.Coefficient}");
            }
            if (correlations.Note is { Length: > 0 } note)
            {
                builder.AppendLine($"  note: {note}");
            }
        }

        if (profile.TargetCandidates.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine(
                $"POSSIBLE TARGET COLUMNS: {string.Join(", ", profile.TargetCandidates.Take(10))}");
        }

        if (profile.Warnings.Count > 0 || profile.ParseErrors.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("INGESTION WARNINGS");
            foreach (var warning in profile.Warnings.Take(10))
            {
                builder.AppendLine($"  - {warning}");
            }

            foreach (var error in profile.ParseErrors.Take(5))
            {
                builder.AppendLine($"  - parse_error: {error}");
            }
        }

        if (profile.Samples is { Rows.Count: > 0 } samples)
        {
            // A deliberately tiny, truncated sample: enough for the model to reason
            // about shape, never enough to reconstruct the dataset.
            builder.AppendLine();
            builder.AppendLine($"SAMPLE ROWS ({Math.Min(samples.Rows.Count, MaxSampleRowsInContext)} of {profile.RowCount} rows)");
            foreach (var row in samples.Rows.Take(MaxSampleRowsInContext))
            {
                var cells = row.Select(c => Truncate(c, MaxSampleColumnWidth));
                builder.AppendLine("  | " + string.Join(" | ", cells));
            }
        }

        var context = builder.ToString().TrimEnd();

        if (context.Length > _options.MaxContextCharacters)
        {
            _logger.LogWarning(
                "Dataset context truncated from {Original} to {Max} characters",
                context.Length,
                _options.MaxContextCharacters);

            // MaxContextCharacters is documented as a hard ceiling on what the provider
            // receives, so the notice has to fit inside the budget rather than being
            // appended past it.
            context = _options.MaxContextCharacters >= TruncationNotice.Length
                ? context[..(_options.MaxContextCharacters - TruncationNotice.Length)] + TruncationNotice
                : context[.._options.MaxContextCharacters];
        }

        return context;
    }

    // Invariant: this string is consumed by a language model, where "0,5" is ambiguous
    // with a thousands separator and would silently corrupt every statistic quoted.
    private static string R(double? value) =>
        value is null
            ? "n/a"
            : Math.Round(value.Value, 4).ToString("0.####", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}