using Datara.Domain.Enums;

namespace Datara.Application.Analysis;

/// <summary>
/// The deterministic profile returned by the Python analysis service. These
/// records mirror the FastAPI response contract.
/// </summary>
public sealed class DatasetProfileResponse
{
    public int RowCount { get; init; }

    public int ColumnCount { get; init; }

    public int TotalCells { get; init; }

    public int TotalMissing { get; init; }

    public decimal OverallMissingPercentage { get; init; }

    public int DuplicateRowCount { get; init; }

    public decimal DuplicatePercentage { get; init; }

    public List<string> FullyEmptyColumns { get; init; } = new();

    public long MemoryUsageBytes { get; init; }

    public string IngestedAtUtc { get; init; } = string.Empty;

    public List<string> Warnings { get; init; } = new();

    public List<string> ParseErrors { get; init; } = new();

    public List<ColumnProfileResponse> Columns { get; init; } = new();

    public CorrelationMatrixResponse? Correlations { get; init; }

    public List<AnomalyResponse> Anomalies { get; init; } = new();

    public List<QualityIssueResponse> QualityIssues { get; init; } = new();

    public MlReadinessResponse? MlReadiness { get; init; }

    public SampleRowsResponse? Samples { get; init; }

    public List<string> TargetCandidates { get; init; } = new();
}

public sealed class ColumnProfileResponse
{
    public string Name { get; init; } = string.Empty;

    public int Ordinal { get; init; }

    public string InferredKind { get; init; } = string.Empty;

    public string PandasDtype { get; init; } = string.Empty;

    public int NonNullCount { get; init; }

    public int NullCount { get; init; }

    public decimal MissingPercentage { get; init; }

    public int UniqueCount { get; init; }

    public decimal CardinalityRatio { get; init; }

    public bool IsConstant { get; init; }

    public long? MemorySizeBytes { get; init; }

    public NumericSummaryResponse? Numeric { get; init; }

    public List<CategoryFrequencyResponse> TopCategories { get; init; } = new();

    public decimal CategoryPercentageCovered { get; init; }

    public HistogramResponse? Histogram { get; init; }

    public List<OutlierSummaryResponse> Outliers { get; init; } = new();

    public int LeadingWhitespaceCount { get; init; }

    public int MixedTypeCount { get; init; }

    public string? DateMin { get; init; }

    public string? DateMax { get; init; }

    public List<string> SampleValues { get; init; } = new();

    public List<string> Notes { get; init; } = new();
}

public sealed class NumericSummaryResponse
{
    public double? Mean { get; init; }

    public double? Median { get; init; }

    public double? Std { get; init; }

    public double? Min { get; init; }

    public double? Max { get; init; }

    public double? Q1 { get; init; }

    public double? Q3 { get; init; }

    public double? P05 { get; init; }

    public double? P25 { get; init; }

    public double? P75 { get; init; }

    public double? P95 { get; init; }

    public double? Skewness { get; init; }

    public double? Kurtosis { get; init; }

    public double? Sum { get; init; }

    public double? Variance { get; init; }

    public double? Range { get; init; }

    public double? CoefficientOfVariation { get; init; }

    public int ZerosCount { get; init; }

    public int NegativeCount { get; init; }
}

public sealed class CategoryFrequencyResponse
{
    public string Value { get; init; } = string.Empty;

    public int Count { get; init; }

    public decimal Percentage { get; init; }
}

public sealed class HistogramResponse
{
    public List<double> BinEdges { get; init; } = new();

    public List<int> Counts { get; init; } = new();

    public bool Density { get; init; }
}

public sealed class OutlierSummaryResponse
{
    public string Method { get; init; } = string.Empty;

    public double? LowerBound { get; init; }

    public double? UpperBound { get; init; }

    public int Count { get; init; }

    public decimal Percentage { get; init; }

    public string Confidence { get; init; } = string.Empty;
}

public sealed class CorrelationMatrixResponse
{
    public string Method { get; init; } = string.Empty;

    public List<string> Columns { get; init; } = new();

    public List<List<double?>> Values { get; init; } = new();

    public List<CorrelationPairResponse> Pairs { get; init; } = new();

    public string? Note { get; init; }
}

public sealed class CorrelationPairResponse
{
    public string ColumnA { get; init; } = string.Empty;

    public string ColumnB { get; init; } = string.Empty;

    public double Coefficient { get; init; }

    public string Method { get; init; } = string.Empty;
}

public sealed class AnomalyResponse
{
    public string Column { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public Dictionary<string, object?> Evidence { get; init; } = new();
}

public sealed class QualityIssueResponse
{
    public string Code { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string? Column { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public Dictionary<string, object?> Evidence { get; init; } = new();

    public string Recommendation { get; init; } = string.Empty;

    public decimal ScoreImpact { get; init; }
}

public sealed class MlReadinessResponse
{
    public decimal Overall { get; init; }

    public string Grade { get; init; } = string.Empty;

    public string Verdict { get; init; } = string.Empty;

    public List<ScoreComponentResponse> Components { get; init; } = new();

    public decimal DataQualityScore { get; init; }

    public ClassBalanceResponse? ClassBalance { get; init; }

    public List<LeakageSignalResponse> LeakageSignals { get; init; } = new();

    public List<string> Strengths { get; init; } = new();

    public List<string> Risks { get; init; } = new();

    public List<string> Gates { get; init; } = new();
}

public sealed class ScoreComponentResponse
{
    public string Key { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public decimal Score { get; init; }

    public decimal Weight { get; init; }

    public string Rationale { get; init; } = string.Empty;

    public List<ScorePenaltyResponse> Penalties { get; init; } = new();
}

public sealed class ScorePenaltyResponse
{
    public decimal Points { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public Dictionary<string, object?> Evidence { get; init; } = new();
}

public sealed class ClassBalanceResponse
{
    public string Column { get; init; } = string.Empty;

    public int DistinctClasses { get; init; }

    public decimal MinorityPercentage { get; init; }

    public decimal MajorityPercentage { get; init; }

    public decimal? ImbalanceRatio { get; init; }

    public bool IsImbalanced { get; init; }
}

public sealed class LeakageSignalResponse
{
    public string Column { get; init; } = string.Empty;

    public string Signal { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public string Rationale { get; init; } = string.Empty;

    public Dictionary<string, object?> Evidence { get; init; } = new();
}

public sealed class SampleRowsResponse
{
    public List<string> Columns { get; init; } = new();

    public List<List<string>> Rows { get; init; } = new();

    public bool Truncated { get; init; }
}

/// <summary>Error envelope returned by the analysis service.</summary>
public sealed class AnalysisServiceErrorResponse
{
    public AnalysisServiceErrorBody? Error { get; init; }
}

public sealed class AnalysisServiceErrorBody
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}