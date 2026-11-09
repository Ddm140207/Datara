namespace Datara.Domain.Enums;

/// <summary>Trigger that caused an <see cref="Entities.AnalysisRun"/>.</summary>
public enum AnalysisRunTrigger
{
    /// <summary>Run was queued automatically after an upload.</summary>
    Upload = 0,

    /// <summary>Run was requested by a user through the API.</summary>
    Manual = 1,

    /// <summary>Run was triggered by a schedule (future capability).</summary>
    Scheduled = 2,
}