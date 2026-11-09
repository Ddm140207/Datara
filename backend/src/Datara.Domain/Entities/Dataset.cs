using Datara.Domain.Enums;

namespace Datara.Domain.Entities;

/// <summary>An uploaded dataset and its headline metadata.</summary>
public class Dataset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OwnerId { get; set; }

    public User? Owner { get; set; }

    /// <summary>User-facing display name. Never derived from an untrusted file path.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Sanitised original filename, retained for display and download.</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>Relative storage key of the uploaded payload. Never client supplied.</summary>
    public string StorageKey { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    /// <summary>Detected content type of the upload.</summary>
    public string ContentType { get; set; } = "text/csv";

    /// <summary>Lower-cased format discriminator used to pick a future reader.</summary>
    public string Format { get; set; } = "csv";

    public int RowCount { get; set; }

    public int ColumnCount { get; set; }

    public AnalysisStatus Status { get; set; } = AnalysisStatus.Pending;

    /// <summary>ML readiness score from 0-100, or null until analysis completes.</summary>
    public decimal? MlReadinessScore { get; set; }

    /// <summary>Data quality score from 0-100, or null until analysis completes.</summary>
    public decimal? DataQualityScore { get; set; }

    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? AnalyzedAt { get; set; }

    /// <summary>Optional target column hint supplied by the user at upload time.</summary>
    public string? TargetColumn { get; set; }

    public string? Description { get; set; }

    public ICollection<DatasetColumn> Columns { get; set; } = new List<DatasetColumn>();

    public ICollection<DatasetAnalysis> Analyses { get; set; } = new List<DatasetAnalysis>();

    public ICollection<DataQualityIssue> Issues { get; set; } = new List<DataQualityIssue>();

    public ICollection<AnalysisRun> Runs { get; set; } = new List<AnalysisRun>();
}