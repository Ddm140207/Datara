namespace Datara.Application.Datasets.Dtos;

public sealed record DatasetListItemDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string OriginalFileName { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public long FileSizeBytes { get; init; }

    public int RowCount { get; init; }

    public int ColumnCount { get; init; }

    public decimal? MlReadinessScore { get; init; }

    public decimal? DataQualityScore { get; init; }

    public DateTimeOffset UploadedAt { get; init; }
}

public sealed record DatasetDetailDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string OriginalFileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;

    public string Format { get; init; } = string.Empty;

    public long FileSizeBytes { get; init; }

    public int RowCount { get; init; }

    public int ColumnCount { get; init; }

    public string Status { get; init; } = string.Empty;

    public decimal? MlReadinessScore { get; init; }

    public decimal? DataQualityScore { get; init; }

    public string? Description { get; init; }

    public string? TargetColumn { get; init; }

    public DateTimeOffset UploadedAt { get; init; }

    public DateTimeOffset? AnalyzedAt { get; init; }

    public int IssueCount { get; init; }

    public int CriticalIssueCount { get; init; }
}

public sealed record UploadDatasetResponse
{
    public DatasetDetailDto Dataset { get; init; } = new();

    /// <summary>True when profiling already finished before this response was written.</summary>
    public bool AnalysisCompleted { get; init; }
}

public sealed record CreateDatasetRequest
{
    public string? Name { get; init; }

    /// <summary>Optional target column name, improves class balance and leakage analysis.</summary>
    public string? TargetColumn { get; init; }

    public string? Description { get; init; }
}

public sealed record RenameDatasetRequest
{
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }
}

public sealed record DeleteDatasetResult
{
    public Guid Id { get; init; }

    public bool Deleted { get; init; }
}