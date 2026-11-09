namespace Datara.Domain.Entities;

/// <summary>A single deduction applied to a score component.</summary>
public class ScorePenalty
{
    public decimal Points { get; set; }

    /// <summary>Machine-readable reason code, for example "missing_values".</summary>
    public string Reason { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;
}