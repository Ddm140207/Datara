namespace Datara.Application.Datasets.Dtos;

public sealed record DatasetOverviewDto
{
    public Guid DatasetId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    // ---- headline metrics ---------------------------------------------------
    public MlReadinessDto Readiness { get; init; } = new();

    public DatasetStatisticsDto Statistics { get; init; } = new();

    public List<ColumnDto> Columns { get; init; } = new();

    public List<QualityIssueDto> Issues { get; init; } = new();

    public IssueSeverityBreakdownDto IssueBreakdown { get; init; } = new();

    public List<RecommendedActionDto> RecommendedActions { get; init; } = new();

    public List<AnomalyDto> Anomalies { get; init; } = new();

    public CorrelationMatrixDto? Correlations { get; init; }

    public ClassBalanceDto? ClassBalance { get; init; }

    public List<LeakageSignalDto> LeakageSignals { get; init; } = new();

    public List<string> TargetCandidates { get; init; } = new();

    public List<string> Warnings { get; init; } = new();

    public DateTimeOffset? AnalyzedAt { get; init; }
}

public sealed record MlReadinessDto
{
    public decimal Score { get; init; }

    public decimal DataQualityScore { get; init; }

    public string Grade { get; init; } = string.Empty;

    public string Verdict { get; init; } = string.Empty;

    public List<ScoreComponentDto> Components { get; init; } = new();

    public List<string> Strengths { get; init; } = new();

    public List<string> Risks { get; init; } = new();

    public List<string> Gates { get; init; } = new();
}

public sealed record ScoreComponentDto
{
    public string Key { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public decimal Score { get; init; }

    public decimal Weight { get; init; }

    public string Rationale { get; init; } = string.Empty;

    public List<ScorePenaltyDto> Penalties { get; init; } = new();
}

public sealed record ScorePenaltyDto
{
    public decimal Points { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;
}

public sealed record DatasetStatisticsDto
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

    public int NumericColumnCount { get; init; }

    public int CategoricalColumnCount { get; init; }

    public int TextColumnCount { get; init; }

    public int ConstantColumnCount { get; init; }
}

public sealed record QualityIssueDto
{
    public Guid Id { get; init; }

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

public sealed record IssueSeverityBreakdownDto
{
    public int Critical { get; init; }

    public int High { get; init; }

    public int Medium { get; init; }

    public int Low { get; init; }

    public int Info { get; init; }
}

public sealed record RecommendedActionDto
{
    /// <summary>Stable machine-readable action key, for example "impute_column".</summary>
    public string Key { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public string? Column { get; init; }

    /// <summary>Coarse grouping used to order the action list in the UI.</summary>
    public string Category { get; init; } = string.Empty;
}

public sealed record AnomalyDto
{
    public string Column { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;
}

public sealed record CorrelationMatrixDto
{
    public string Method { get; init; } = string.Empty;

    public List<string> Columns { get; init; } = new();

    public List<List<decimal?>> Values { get; init; } = new();

    public List<CorrelationPairDto> Pairs { get; init; } = new();

    public string? Note { get; init; }
}

public sealed record CorrelationPairDto
{
    public string ColumnA { get; init; } = string.Empty;

    public string ColumnB { get; init; } = string.Empty;

    public decimal Coefficient { get; init; }

    public string Method { get; init; } = string.Empty;
}

public sealed record ClassBalanceDto
{
    public string Column { get; init; } = string.Empty;

    public int DistinctClasses { get; init; }

    public decimal MinorityPercentage { get; init; }

    public decimal MajorityPercentage { get; init; }

    public decimal? ImbalanceRatio { get; init; }

    public bool IsImbalanced { get; init; }
}

public sealed record LeakageSignalDto
{
    public string Column { get; init; } = string.Empty;

    public string Signal { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public string Rationale { get; init; } = string.Empty;
}