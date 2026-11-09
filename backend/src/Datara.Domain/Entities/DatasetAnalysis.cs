namespace Datara.Domain.Entities;

/// <summary>
/// The immutable result of one profiling pass. Kept separate from
/// <see cref="Dataset"/> so the dashboard can load a known-good analysis while a
/// re-analysis is in flight.
/// </summary>
public class DatasetAnalysis
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DatasetId { get; set; }

    public Dataset? Dataset { get; set; }

    /// <summary>Links this analysis back to the run that produced it.</summary>
    public Guid? AnalysisRunId { get; set; }

    public AnalysisRun? AnalysisRun { get; set; }

    public int RowCount { get; set; }

    public int ColumnCount { get; set; }

    public int TotalCells { get; set; }

    public int TotalMissing { get; set; }

    public decimal OverallMissingPercentage { get; set; }

    public int DuplicateRowCount { get; set; }

    public decimal DuplicatePercentage { get; set; }

    public List<string> FullyEmptyColumns { get; set; } = new();

    public long MemoryUsageBytes { get; set; }

    public decimal MlReadinessScore { get; set; }

    public decimal DataQualityScore { get; set; }

    /// <summary>Letter grade A-F derived from the readiness score.</summary>
    public string Grade { get; set; } = string.Empty;

    /// <summary>Short human-readable verdict, for example "Ready for modelling".</summary>
    public string Verdict { get; set; } = string.Empty;

    public List<ScoreComponent> ScoreComponents { get; set; } = new();

    public List<string> Strengths { get; set; } = new();

    public List<string> Risks { get; set; } = new();

    /// <summary>Reasons the overall score was capped regardless of weighted average.</summary>
    public List<string> Gates { get; set; } = new();

    public CorrelationMatrixRecord? Correlations { get; set; }

    public List<AnomalyRecord> Anomalies { get; set; } = new();

    public ClassBalanceRecord? ClassBalance { get; set; }

    public List<LeakageSignal> LeakageSignals { get; set; } = new();

    public List<string> TargetCandidates { get; set; } = new();

    /// <summary>Representative rows retained for the AI context and the preview grid.</summary>
    public List<string> SampleRows { get; set; } = new();

    public List<string> SampleColumns { get; set; } = new();

    public List<string> Warnings { get; set; } = new();

    public List<string> ParseErrors { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}