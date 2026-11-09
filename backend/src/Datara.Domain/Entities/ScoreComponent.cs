namespace Datara.Domain.Entities;

/// <summary>One weighted dimension of the ML readiness score.</summary>
public class ScoreComponent
{
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public decimal Score { get; set; }

    public decimal Weight { get; set; }

    /// <summary>Plain-language explanation of how this score was reached.</summary>
    public string Rationale { get; set; } = string.Empty;

    /// <summary>Individual deductions, so the number can be audited end to end.</summary>
    public List<ScorePenalty> Penalties { get; set; } = new();
}