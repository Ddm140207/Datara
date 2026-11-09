namespace Datara.Domain.Entities;

/// <summary>A detected risk that a column leaks the prediction target.</summary>
public class LeakageSignal
{
    public string Column { get; set; } = string.Empty;

    /// <summary>Machine-readable signal kind, for example "duplicate_column".</summary>
    public string Signal { get; set; } = string.Empty;

    public string Severity { get; set; } = "Low";

    public string Rationale { get; set; } = string.Empty;
}