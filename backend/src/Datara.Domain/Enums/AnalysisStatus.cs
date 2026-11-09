namespace Datara.Domain.Enums;

/// <summary>Lifecycle of a dataset as it moves from upload through analysis.</summary>
public enum AnalysisStatus
{
    /// <summary>File accepted, analysis has not started.</summary>
    Pending = 0,

    /// <summary>Profiling is currently running.</summary>
    Running = 1,

    /// <summary>Analysis completed and results are persisted.</summary>
    Completed = 2,

    /// <summary>Analysis failed. See <see cref="DatasetAnalysis.ErrorMessage"/>.</summary>
    Failed = 3,
}