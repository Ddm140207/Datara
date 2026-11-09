namespace Datara.Domain.Enums;

/// <summary>Outcome of an individual <see cref="Entities.AnalysisRun"/>.</summary>
public enum AnalysisRunStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
}