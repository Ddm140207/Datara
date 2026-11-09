namespace Datara.Application.Datasets.Dtos;

/// <summary>Arguments for uploading a dataset.</summary>
public sealed record UploadDatasetCommand
{
    /// <summary>Client-supplied filename. Never trusted for filesystem paths.</summary>
    public string OriginalFileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = "text/csv";

    public byte[] Content { get; init; } = Array.Empty<byte>();

    public string? Name { get; init; }

    public string? TargetColumn { get; init; }

    public string? Description { get; init; }
}

/// <summary>Arguments for re-running profiling on an existing dataset.</summary>
public sealed record AnalyseDatasetCommand
{
    public string? TargetColumn { get; init; }
}

public sealed record SampleRowsDto
{
    public List<string> Columns { get; init; } = new();

    public List<List<string>> Rows { get; init; } = new();

    public bool Truncated { get; init; }
}

public sealed record AnalysisRunDto
{
    public Guid Id { get; init; }

    public Guid DatasetId { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Trigger { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public long? DurationMs { get; init; }

    public string? ErrorMessage { get; init; }
}