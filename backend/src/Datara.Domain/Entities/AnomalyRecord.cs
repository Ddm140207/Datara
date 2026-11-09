namespace Datara.Domain.Entities;

/// <summary>A statistical oddity observed on a single column.</summary>
public class AnomalyRecord
{
    public string Column { get; set; } = string.Empty;

    /// <summary>Machine-readable anomaly kind, for example "outliers_iqr".</summary>
    public string Kind { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Severity { get; set; } = "Medium";
}