using Datara.Domain.Enums;

namespace Datara.Domain.Entities;

/// <summary>
/// An audit record for one profiling attempt. Kept even when the run fails so
/// operators can diagnose analysis problems after the fact.
/// </summary>
public class AnalysisRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DatasetId { get; set; }

    public Dataset? Dataset { get; set; }

    public AnalysisRunStatus Status { get; set; } = AnalysisRunStatus.Queued;

    public AnalysisRunTrigger Trigger { get; set; } = AnalysisRunTrigger.Upload;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Wall-clock duration of the run in milliseconds.</summary>
    public long? DurationMs { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Correlates the run with logs in the analysis service.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    public ICollection<DatasetAnalysis> Analyses { get; set; } = new List<DatasetAnalysis>();
}