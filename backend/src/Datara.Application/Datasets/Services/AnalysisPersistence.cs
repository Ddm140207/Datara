using System.Globalization;
using System.Text.Json;
using Datara.Application.Analysis;
using Datara.Domain.Entities;
using Datara.Domain.Enums;

namespace Datara.Application.Datasets.Services;

/// <summary>
/// Translates the Python analysis service payload into persistence entities.
/// Keeping this mapping in one place means a change to the wire contract surfaces
/// here rather than being smeared through the service and repository layers.
/// </summary>
internal static class AnalysisPersistence
{
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static DatasetAnalysis Map(Guid datasetId, Guid runId, DatasetProfileResponse profile)
    {
        var readiness = profile.MlReadiness;

        return new DatasetAnalysis
        {
            DatasetId = datasetId,
            AnalysisRunId = runId,
            RowCount = profile.RowCount,
            ColumnCount = profile.ColumnCount,
            TotalCells = profile.TotalCells,
            TotalMissing = profile.TotalMissing,
            OverallMissingPercentage = profile.OverallMissingPercentage,
            DuplicateRowCount = profile.DuplicateRowCount,
            DuplicatePercentage = profile.DuplicatePercentage,
            FullyEmptyColumns = [.. profile.FullyEmptyColumns],
            MemoryUsageBytes = profile.MemoryUsageBytes,
            MlReadinessScore = readiness?.Overall ?? 0m,
            DataQualityScore = readiness?.DataQualityScore ?? 0m,
            Grade = readiness?.Grade ?? "F",
            Verdict = readiness?.Verdict ?? "Analysis produced no readiness verdict.",
            ScoreComponents = readiness?.Components.Select(MapComponent).ToList() ?? [],
            Strengths = readiness?.Strengths.ToList() ?? [],
            Risks = readiness?.Risks.ToList() ?? [],
            Gates = readiness?.Gates.ToList() ?? [],
            Correlations = profile.Correlations is null ? null : MapCorrelations(profile.Correlations),
            Anomalies = profile.Anomalies.Select(MapAnomaly).ToList(),
            ClassBalance = readiness?.ClassBalance is { } balance ? MapBalance(balance) : null,
            LeakageSignals = readiness?.LeakageSignals
                .Select(s => new LeakageSignal
                {
                    Column = s.Column,
                    Signal = s.Signal,
                    Severity = s.Severity,
                    Rationale = s.Rationale,
                })
                .ToList() ?? [],
            TargetCandidates = [.. profile.TargetCandidates],
            SampleColumns = profile.Samples?.Columns.ToList() ?? [],
            SampleRows = EncodeSampleRows(profile.Samples),
            Warnings = [.. profile.Warnings],
            ParseErrors = [.. profile.ParseErrors],
            ErrorMessage = null,
            CreatedAt = ParseIngestedAt(profile.IngestedAtUtc),
        };
    }

    /// <summary>Records a failed run so the UI can explain why no results exist.</summary>
    public static DatasetAnalysis MapFailure(Guid datasetId, Guid runId, string error) => new()
    {
        DatasetId = datasetId,
        AnalysisRunId = runId,
        ErrorMessage = error.Length > 2000 ? error[..2000] : error,
        Verdict = "Analysis failed.",
    };

    public static List<DatasetColumn> MapColumns(Guid datasetId, DatasetProfileResponse profile) =>
        profile.Columns.Select(c => MapColumn(datasetId, c)).ToList();

    public static List<DataQualityIssue> MapIssues(
        Guid datasetId,
        Guid analysisId,
        DatasetProfileResponse profile) =>
        profile.QualityIssues
            .Select(issue => new DataQualityIssue
            {
                DatasetId = datasetId,
                DatasetAnalysisId = analysisId,
                Code = issue.Code,
                Severity = ParseSeverity(issue.Severity),
                Category = ParseCategory(issue.Category),
                Column = issue.Column,
                Title = issue.Title,
                Description = issue.Description,
                EvidenceJson = JsonSerializer.Serialize(issue.Evidence, EvidenceJsonOptions),
                Recommendation = issue.Recommendation,
                ScoreImpact = issue.ScoreImpact,
            })
            .ToList();

    private static DatasetColumn MapColumn(Guid datasetId, ColumnProfileResponse column) => new()
    {
        DatasetId = datasetId,
        Name = column.Name,
        Ordinal = column.Ordinal,
        InferredKind = ParseKind(column.InferredKind),
        PandasDtype = column.PandasDtype,
        NonNullCount = column.NonNullCount,
        NullCount = column.NullCount,
        MissingPercentage = column.MissingPercentage,
        UniqueCount = column.UniqueCount,
        CardinalityRatio = column.CardinalityRatio,
        IsConstant = column.IsConstant,
        MemorySizeBytes = column.MemorySizeBytes,
        LeadingWhitespaceCount = column.LeadingWhitespaceCount,
        MixedTypeCount = column.MixedTypeCount,

        Mean = Dec(column.Numeric?.Mean),
        Median = Dec(column.Numeric?.Median),
        StdDev = Dec(column.Numeric?.Std),
        Min = Dec(column.Numeric?.Min),
        Max = Dec(column.Numeric?.Max),
        Q1 = Dec(column.Numeric?.Q1),
        Q3 = Dec(column.Numeric?.Q3),
        P05 = Dec(column.Numeric?.P05),
        P25 = Dec(column.Numeric?.P25),
        P75 = Dec(column.Numeric?.P75),
        P95 = Dec(column.Numeric?.P95),
        Skewness = Dec(column.Numeric?.Skewness),
        Kurtosis = Dec(column.Numeric?.Kurtosis),
        ZerosCount = column.Numeric?.ZerosCount,
        NegativeCount = column.Numeric?.NegativeCount,

        TopCategories = column.TopCategories
            .Select(c => new CategoryFrequency
            {
                Value = c.Value,
                Count = c.Count,
                Percentage = c.Percentage,
            })
            .ToList(),
        CategoryPercentageCovered = column.CategoryPercentageCovered,
        HistogramBinEdges = column.Histogram?.BinEdges.ToList() ?? [],
        HistogramCounts = column.Histogram?.Counts.ToList() ?? [],
        Outliers = column.Outliers
            .Select(o => new OutlierRecord
            {
                Method = o.Method,
                LowerBound = Dec(o.LowerBound),
                UpperBound = Dec(o.UpperBound),
                Count = o.Count,
                Percentage = o.Percentage,
                Confidence = o.Confidence,
            })
            .ToList(),
        DateMin = ParseDate(column.DateMin),
        DateMax = ParseDate(column.DateMax),
        SampleValues = [.. column.SampleValues],
        Notes = [.. column.Notes],
    };

    private static ScoreComponent MapComponent(ScoreComponentResponse component) => new()
    {
        Key = component.Key,
        Label = component.Label,
        Score = component.Score,
        Weight = component.Weight,
        Rationale = component.Rationale,
        Penalties = component.Penalties
            .Select(p => new ScorePenalty
            {
                Points = p.Points,
                Reason = p.Reason,
                Detail = p.Detail,
            })
            .ToList(),
    };

    private static CorrelationMatrixRecord MapCorrelations(CorrelationMatrixResponse matrix) => new()
    {
        Method = matrix.Method,
        Columns = [.. matrix.Columns],
        Values = matrix.Values
            .Select(row => row.Select(Dec).ToList())
            .ToList(),
        Pairs = matrix.Pairs
            .Select(p => new CorrelationPairRecord
            {
                ColumnA = p.ColumnA,
                ColumnB = p.ColumnB,
                Coefficient = Dec(p.Coefficient),
                Method = p.Method,
            })
            .ToList(),
        Note = matrix.Note,
    };

    private static AnomalyRecord MapAnomaly(AnomalyResponse anomaly) => new()
    {
        Column = anomaly.Column,
        Kind = anomaly.Kind,
        Description = anomaly.Description,
        Severity = anomaly.Severity,
    };

    private static ClassBalanceRecord MapBalance(ClassBalanceResponse balance) => new()
    {
        Column = balance.Column,
        DistinctClasses = balance.DistinctClasses,
        MinorityPercentage = balance.MinorityPercentage,
        MajorityPercentage = balance.MajorityPercentage,
        ImbalanceRatio = balance.ImbalanceRatio,
        IsImbalanced = balance.IsImbalanced,
    };

    /// <summary>Sample rows are stored one JSON array per line to keep queries cheap.</summary>
    private static List<string> EncodeSampleRows(SampleRowsResponse? samples)
    {
        if (samples is null || samples.Rows.Count == 0)
        {
            return [];
        }

        return samples.Rows
            .Select(row => JsonSerializer.Serialize(row, EvidenceJsonOptions))
            .ToList();
    }

    /// <summary>Inverse of <see cref="EncodeSampleRows"/>, tolerant of corrupt lines.</summary>
    public static List<List<string>> DecodeSampleRows(IEnumerable<string> rows)
    {
        var decoded = new List<List<string>>();

        foreach (var row in rows)
        {
            try
            {
                var cells = JsonSerializer.Deserialize<List<string>>(row);
                if (cells is not null)
                {
                    decoded.Add(cells);
                }
            }
            catch (JsonException)
            {
                // A malformed persisted row must not break the whole preview endpoint.
            }
        }

        return decoded;
    }

    /// <summary>
    /// Narrows a JSON double to the decimal used by the domain model, guarding the
    /// NaN/infinity values pandas can emit for degenerate columns.
    /// </summary>
    private static decimal? Dec(double? value) =>
        value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value)
            ? null
            : (decimal?)Math.Round(value.Value, 6);

    private static decimal Dec(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? 0m : Math.Round((decimal)value, 6);

    /// <summary>
    /// Zone-less timestamps from the analysis service are read as UTC rather than
    /// local time, so a dataset profiles identically on every server. Postgres also
    /// rejects a non-zero offset on <c>timestamptz</c>, so this also keeps the write
    /// path valid.
    /// </summary>
    private static DateTimeOffset ParseIngestedAt(string value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

    private static DateTimeOffset? ParseDate(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || !DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? null
            : parsed;

    private static ColumnKind ParseKind(string value) =>
        Enum.TryParse<ColumnKind>(value, ignoreCase: true, out var parsed) ? parsed : ColumnKind.Unknown;

    private static IssueSeverity ParseSeverity(string value) =>
        Enum.TryParse<IssueSeverity>(value, ignoreCase: true, out var parsed)
            ? parsed
            : IssueSeverity.Info;

    /// <summary>
    /// Unknown categories map to <see cref="IssueCategory.Consistency"/> rather than
    /// throwing, so a new category added by the analysis service cannot break ingest.
    /// </summary>
    private static IssueCategory ParseCategory(string value) =>
        Enum.TryParse<IssueCategory>(value, ignoreCase: true, out var parsed)
            ? parsed
            : IssueCategory.Consistency;
}