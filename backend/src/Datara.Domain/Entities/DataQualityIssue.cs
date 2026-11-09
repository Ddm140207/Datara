using Datara.Domain.Enums;

namespace Datara.Domain.Entities;

/// <summary>A single detected data quality issue.</summary>
public class DataQualityIssue
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DatasetId { get; set; }

    public Dataset? Dataset { get; set; }

    public Guid? DatasetAnalysisId { get; set; }

    public DatasetAnalysis? DatasetAnalysis { get; set; }

    /// <summary>Stable machine-readable identifier, for example "missing_values::income".</summary>
    public string Code { get; set; } = string.Empty;

    public IssueSeverity Severity { get; set; } = IssueSeverity.Info;

    public IssueCategory Category { get; set; }

    /// <summary>Affected column, or null for dataset-level issues.</summary>
    public string? Column { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Supporting numbers, serialised as JSON.</summary>
    public string EvidenceJson { get; set; } = "{}";

    public string Recommendation { get; set; } = string.Empty;

    /// <summary>Relative contribution this issue made to lowering the score.</summary>
    public decimal ScoreImpact { get; set; }

    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
}