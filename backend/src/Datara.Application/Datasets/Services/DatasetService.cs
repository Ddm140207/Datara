using System.Text.Json;
using System.Text.Json.Serialization;
using Datara.Application.Analysis;
using Datara.Application.Ai.Abstractions;
using Datara.Application.Common;
using Datara.Application.Datasets.Abstractions;
using Datara.Application.Datasets.Dtos;
using Datara.Domain.Enums;
using Datara.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Datara.Application.Datasets.Services;

/// <summary>
/// Application service for dataset ingestion, analysis and querying.
///
/// Controllers delegate here and stay thin. This layer owns the workflow:
/// validate the upload, persist it, orchestrate the Python analysis service,
/// persist the results, and project entities into DTOs. Database entities are
/// never returned directly.
/// </summary>
public sealed class DatasetService : IDatasetService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv", ".tsv", ".txt",
    };

    private static readonly string[] AllowedContentTypes =
    {
        "text/csv", "application/csv", "text/plain", "text/tab-separated-values",
        "application/vnd.ms-excel", "application/octet-stream",
    };

    private readonly IDatasetRepository _repository;
    private readonly IAnalysisServiceClient _analysisClient;
    private readonly IFileStorage _fileStorage;
    private readonly IClock _clock;
    private readonly ILogger<DatasetService> _logger;
    private readonly AnalysisServiceOptions _analysisOptions;

    public DatasetService(
        IDatasetRepository repository,
        IAnalysisServiceClient analysisClient,
        IFileStorage fileStorage,
        IClock clock,
        IOptions<AnalysisServiceOptions> analysisOptions,
        ILogger<DatasetService> logger)
    {
        _repository = repository;
        _analysisClient = analysisClient;
        _fileStorage = fileStorage;
        _clock = clock;
        _logger = logger;
        _analysisOptions = analysisOptions.Value;
    }

    public async Task<UploadDatasetResponse> UploadAsync(
        UploadDatasetCommand command,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        ValidateUpload(command);

        var dataset = new Dataset
        {
            OwnerId = ownerId,
            Name = ResolveName(command),
            OriginalFileName = SanitiseFileName(command.OriginalFileName),

            // Storage keys are generated server-side from validated ids only. The
            // client filename is stored for display and never touches the key.
            StorageKey = StorageKeyFactory.ForDataset(ownerId, Guid.NewGuid(), Path.GetExtension(command.OriginalFileName)),
            FileSizeBytes = command.Content.LongLength,
            ContentType = command.ContentType,
            Format = "csv",
            Status = AnalysisStatus.Pending,
            TargetColumn = command.TargetColumn?.Trim(),
            Description = command.Description?.Trim(),
            UploadedAt = _clock.UtcNow,
        };

        await _fileStorage.SaveAsync(dataset.StorageKey, command.Content, cancellationToken);
        await _repository.AddAsync(dataset, cancellationToken);
        _logger.LogInformation(
            "Accepted dataset {DatasetId} for owner {OwnerId} ({Bytes} bytes)",
            dataset.Id,
            ownerId,
            dataset.FileSizeBytes);

        var completed = await RunAnalysisAsync(dataset, command.TargetColumn, AnalysisRunTrigger.Upload, cancellationToken);

        // Counts come from the persisted issues so the response matches what a
        // follow-up GET returns.
        var issues = completed
            ? await _repository.GetIssuesAsync(dataset.Id, cancellationToken)
            : [];

        return new UploadDatasetResponse
        {
            Dataset = ToDetail(dataset, issues.Count, issues.Count(i => i.Severity == IssueSeverity.Critical)),
            AnalysisCompleted = completed,
        };
    }

    public async Task<PagedResult<DatasetListItemDto>> ListAsync(
        string? query,
        Guid ownerId,
        PageOptions page,
        CancellationToken cancellationToken)
    {
        var normalisedPage = page.NormalisedPage;
        var pageSize = page.NormalisedPageSize;
        var skip = (normalisedPage - 1) * pageSize;

        var items = await _repository.SearchAsync(query, ownerId, skip, pageSize, cancellationToken);
        var total = await _repository.CountAsync(query, ownerId, cancellationToken);

        return new PagedResult<DatasetListItemDto>(
            items.Select(ToListItem).ToList(),
            normalisedPage,
            pageSize,
            total);
    }

    public async Task<DatasetDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var dataset = await LoadAsync(id, cancellationToken);
        var issues = await _repository.GetIssuesAsync(id, cancellationToken);
        return ToDetail(dataset, issues.Count, issues.Count(i => i.Severity == IssueSeverity.Critical));
    }

    public async Task<DatasetOverviewDto> GetOverviewAsync(Guid id, CancellationToken cancellationToken)
    {
        var dataset = await LoadAsync(id, cancellationToken);
        var analysis = await _repository.GetLatestAnalysisAsync(id, cancellationToken);

        if (analysis is null)
        {
            return PendingOverview(dataset);
        }

        var columns = await _repository.GetColumnsAsync(id, cancellationToken);
        var issues = await _repository.GetIssuesAsync(id, cancellationToken);
        var issueDtos = issues.Select(ToIssueDto).ToList();

        return new DatasetOverviewDto
        {
            DatasetId = dataset.Id,
            Name = dataset.Name,
            Status = EnumText.Of(dataset.Status),
            Readiness = MlReadinessMapper.ToDto(analysis),
            Statistics = BuildStatistics(dataset, analysis, columns),
            Columns = columns.Select(ColumnMapper.ToDto).ToList(),
            Issues = issueDtos,
            IssueBreakdown = Breakdown(issueDtos),
            RecommendedActions = RecommendationBuilder.Build(issueDtos, analysis),
            Anomalies = analysis.Anomalies.Select(ToAnomalyDto).ToList(),
            Correlations = analysis.Correlations is null
                ? null
                : new CorrelationMatrixDto
                {
                    Method = analysis.Correlations.Method,
                    Columns = analysis.Correlations.Columns,
                    Values = analysis.Correlations.Values
                        .Select(row => row.Select(v => v).ToList())
                        .ToList(),
                    Pairs = analysis.Correlations.Pairs
                        .Select(p => new CorrelationPairDto
                        {
                            ColumnA = p.ColumnA,
                            ColumnB = p.ColumnB,
                            Coefficient = p.Coefficient,
                            Method = p.Method,
                        })
                        .ToList(),
                    Note = analysis.Correlations.Note,
                },
            ClassBalance = analysis.ClassBalance is null
                ? null
                : new ClassBalanceDto
                {
                    Column = analysis.ClassBalance.Column,
                    DistinctClasses = analysis.ClassBalance.DistinctClasses,
                    MinorityPercentage = analysis.ClassBalance.MinorityPercentage,
                    MajorityPercentage = analysis.ClassBalance.MajorityPercentage,
                    ImbalanceRatio = analysis.ClassBalance.ImbalanceRatio,
                    IsImbalanced = analysis.ClassBalance.IsImbalanced,
                },
            LeakageSignals = analysis.LeakageSignals
                .Select(s => new LeakageSignalDto
                {
                    Column = s.Column,
                    Signal = s.Signal,
                    Severity = s.Severity,
                    Rationale = s.Rationale,
                })
                .ToList(),
            TargetCandidates = analysis.TargetCandidates,
            Warnings = analysis.Warnings,
            AnalyzedAt = analysis.CreatedAt,
        };
    }

    public async Task<IReadOnlyList<ColumnDto>> GetColumnsAsync(Guid id, CancellationToken cancellationToken)
    {
        await EnsureExistsAsync(id, cancellationToken);
        var columns = await _repository.GetColumnsAsync(id, cancellationToken);
        return columns.Select(ColumnMapper.ToDto).ToList();
    }

    public async Task<ColumnDto> GetColumnAsync(
        Guid id,
        string columnName,
        CancellationToken cancellationToken)
    {
        await EnsureExistsAsync(id, cancellationToken);
        var column = await _repository.GetColumnAsync(id, columnName, cancellationToken);
        if (column is null)
        {
            throw new NotFoundException("Column", columnName);
        }

        return ColumnMapper.ToDto(column);
    }

    public async Task<PagedResult<QualityIssueDto>> GetIssuesAsync(
        Guid id,
        string? severity,
        string? category,
        string? column,
        PageOptions page,
        CancellationToken cancellationToken)
    {
        await EnsureExistsAsync(id, cancellationToken);
        var issues = await _repository.GetIssuesAsync(id, cancellationToken);

        var filtered = issues.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(severity) &&
            Enum.TryParse<IssueSeverity>(severity, ignoreCase: true, out var parsedSeverity))
        {
            filtered = filtered.Where(i => i.Severity == parsedSeverity);
        }

        if (!string.IsNullOrWhiteSpace(category) &&
            Enum.TryParse<IssueCategory>(category, ignoreCase: true, out var parsedCategory))
        {
            filtered = filtered.Where(i => i.Category == parsedCategory);
        }

        if (!string.IsNullOrWhiteSpace(column))
        {
            filtered = filtered.Where(i =>
                string.Equals(i.Column, column, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = filtered
            .OrderByDescending(i => i.Severity)
            .ThenByDescending(i => i.ScoreImpact)
            .ThenBy(i => i.Code, StringComparer.Ordinal)
            .ToList();

        var normalisedPage = page.NormalisedPage;
        var pageSize = page.NormalisedPageSize;
        var items = ordered
            .Skip((normalisedPage - 1) * pageSize)
            .Take(pageSize)
            .Select(ToIssueDto)
            .ToList();

        return new PagedResult<QualityIssueDto>(items, normalisedPage, pageSize, ordered.Count);
    }

    public async Task<CorrelationMatrixDto?> GetCorrelationsAsync(Guid id, CancellationToken cancellationToken)
    {
        var analysis = await _repository.GetLatestAnalysisAsync(id, cancellationToken);
        if (analysis?.Correlations is null)
        {
            return null;
        }

        return new CorrelationMatrixDto
        {
            Method = analysis.Correlations.Method,
            Columns = analysis.Correlations.Columns,
            Values = analysis.Correlations.Values.Select(r => r.ToList()).ToList(),
            Pairs = analysis.Correlations.Pairs
                .Select(p => new CorrelationPairDto
                {
                    ColumnA = p.ColumnA,
                    ColumnB = p.ColumnB,
                    Coefficient = p.Coefficient,
                    Method = p.Method,
                })
                .ToList(),
            Note = analysis.Correlations.Note,
        };
    }

    public async Task<SampleRowsDto?> GetSamplesAsync(
        Guid id,
        int limit,
        CancellationToken cancellationToken)
    {
        await EnsureExistsAsync(id, cancellationToken);
        var analysis = await _repository.GetLatestAnalysisAsync(id, cancellationToken);
        if (analysis is null || analysis.SampleRows.Count == 0)
        {
            return null;
        }

        var capped = Math.Clamp(limit, 1, 100);
        var rows = AnalysisPersistence.DecodeSampleRows(analysis.SampleRows);

        return new SampleRowsDto
        {
            Columns = analysis.SampleColumns,
            Rows = rows.Take(capped).ToList(),
            Truncated = rows.Count > capped,
        };
    }

    public async Task<DatasetDetailDto> ReanalyseAsync(
        Guid id,
        AnalyseDatasetCommand command,
        CancellationToken cancellationToken)
    {
        var dataset = await LoadAsync(id, cancellationToken);

        if (dataset.Status is AnalysisStatus.Running)
        {
            throw new InvalidStateException(
                $"Dataset '{id}' is already being analysed. Wait for the current run to finish.");
        }

        if (!string.IsNullOrWhiteSpace(command.TargetColumn))
        {
            dataset.TargetColumn = command.TargetColumn.Trim();
        }

        await RunAnalysisAsync(dataset, dataset.TargetColumn, AnalysisRunTrigger.Manual, cancellationToken);

        var issues = await _repository.GetIssuesAsync(id, cancellationToken);
        return ToDetail(dataset, issues.Count, issues.Count(i => i.Severity == IssueSeverity.Critical));
    }

    public async Task<DatasetDetailDto> UpdateAsync(
        Guid id,
        RenameDatasetRequest request,
        CancellationToken cancellationToken)
    {
        var dataset = await LoadAsync(id, cancellationToken);

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 200)
        {
            throw new ValidationFailedException("Name must be between 1 and 200 characters.");
        }

        dataset.Name = name;
        dataset.Description = request.Description?.Trim();

        // No repository call: LoadAsync returned a tracked entity, so change detection
        // marks the two mutated properties as Modified on commit. Calling AddAsync here
        // would flip the tracked row to Added and emit an INSERT that collides with its
        // own primary key.
        await SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<DeleteDatasetResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var dataset = await LoadAsync(id, cancellationToken);
        var key = dataset.StorageKey;

        await _repository.RemoveAsync(dataset, cancellationToken);
        await SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(key))
        {
            // Storage is eventually consistent; a failure here must not fail the delete.
            await _fileStorage.DeleteAsync(key, cancellationToken);
        }

        _logger.LogInformation("Deleted dataset {DatasetId}", id);
        return new DeleteDatasetResult { Id = id, Deleted = true };
    }

    public async Task<IReadOnlyList<AnalysisRunDto>> GetRunsAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await EnsureExistsAsync(id, cancellationToken);
        var runs = await _repository.GetRunsAsync(id, cancellationToken);

        return runs
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new AnalysisRunDto
            {
                Id = r.Id,
                DatasetId = r.DatasetId,
                Status = EnumText.Of(r.Status),
                Trigger = EnumText.Of(r.Trigger),
                StartedAt = r.StartedAt,
                CompletedAt = r.CompletedAt,
                DurationMs = r.DurationMs,
                ErrorMessage = r.ErrorMessage,
            })
            .ToList();
    }

    // ------------------------------------------------------------------ //
    // analysis workflow
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Runs profiling for a dataset and persists the outcome. Failures are recorded
    /// on the run and surfaced on the dataset rather than thrown, so a bad file
    /// never leaves a dataset stuck in <see cref="AnalysisStatus.Running"/>.
    /// </summary>
    private async Task<bool> RunAnalysisAsync(
        Dataset dataset,
        string? targetColumn,
        AnalysisRunTrigger trigger,
        CancellationToken cancellationToken)
    {
        var run = new AnalysisRun
        {
            DatasetId = dataset.Id,
            Status = AnalysisRunStatus.Running,
            Trigger = trigger,
            StartedAt = _clock.UtcNow,
            CorrelationId = Guid.NewGuid().ToString("N"),
        };

        dataset.Status = AnalysisStatus.Running;
        await _repository.AddRunAsync(run, cancellationToken);
        await SaveChangesAsync(cancellationToken);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var content = await _fileStorage.ReadAsync(dataset.StorageKey, cancellationToken);
            if (content is null)
            {
                throw new AnalysisFailedException(
                    "The uploaded file could not be retrieved from storage.");
            }

            var profile = await _analysisClient.ProfileAsync(
                content,
                dataset.OriginalFileName,
                includeSamples: true,
                includeCorrelations: true,
                detectOutliers: true,
                targetColumn: targetColumn,
                cancellationToken);

            var analysis = AnalysisPersistence.Map(dataset.Id, run.Id, profile);

            dataset.RowCount = profile.RowCount;
            dataset.ColumnCount = profile.ColumnCount;
            dataset.MlReadinessScore = profile.MlReadiness?.Overall;
            dataset.DataQualityScore = profile.MlReadiness?.DataQualityScore;
            dataset.Status = AnalysisStatus.Completed;
            dataset.AnalyzedAt = _clock.UtcNow;

            run.Status = AnalysisRunStatus.Succeeded;
            run.CompletedAt = _clock.UtcNow;
            run.DurationMs = stopwatch.ElapsedMilliseconds;

            await _repository.SaveAnalysisAsync(
                dataset,
                analysis,
                AnalysisPersistence.MapColumns(dataset.Id, profile),
                AnalysisPersistence.MapIssues(dataset.Id, analysis.Id, profile),
                cancellationToken);
            await SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Analysis {RunId} completed for dataset {DatasetId} in {Duration}ms. " +
                "rows={Rows} columns={Columns} readiness={Score}",
                run.Id,
                dataset.Id,
                stopwatch.ElapsedMilliseconds,
                profile.RowCount,
                profile.ColumnCount,
                profile.MlReadiness?.Overall);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            run.Status = AnalysisRunStatus.Cancelled;
            run.CompletedAt = _clock.UtcNow;
            run.DurationMs = stopwatch.ElapsedMilliseconds;
            dataset.Status = AnalysisStatus.Failed;
            _logger.LogWarning("Analysis {RunId} cancelled for dataset {DatasetId}", run.Id, dataset.Id);
            await TryPersistRunAsync(dataset, run, cancellationToken);
            throw;
        }
        catch (Exception ex) when (ex is UnsupportedFileException or AnalysisFailedException
                                       or AnalysisServiceUnavailableException)
        {
            run.Status = AnalysisRunStatus.Failed;
            run.CompletedAt = _clock.UtcNow;
            run.DurationMs = stopwatch.ElapsedMilliseconds;
            run.ErrorMessage = ex.Message;
            dataset.Status = AnalysisStatus.Failed;

            await _repository.AddRunAsync(run, cancellationToken);
            await _repository.SaveAnalysisAsync(
                dataset,
                AnalysisPersistence.MapFailure(dataset.Id, run.Id, ex.Message),
                [],
                [],
                cancellationToken);
            await SaveChangesAsync(cancellationToken);

            _logger.LogError(ex, "Analysis {RunId} failed for dataset {DatasetId}", run.Id, dataset.Id);
            return false;
        }
    }

    private async Task TryPersistRunAsync(
        Dataset dataset,
        AnalysisRun run,
        CancellationToken cancellationToken)
    {
        try
        {
            dataset.Status = AnalysisStatus.Failed;
            await _repository.AddRunAsync(run, cancellationToken);
            await SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist cancelled run {RunId}", run.Id);
        }
    }

    // ------------------------------------------------------------------ //
    // validation & mapping
    // ------------------------------------------------------------------ //

    private void ValidateUpload(UploadDatasetCommand command)
    {
        if (command.Content.Length == 0)
        {
            throw new ValidationFailedException("The uploaded file is empty.");
        }

        if (command.Content.LongLength > _analysisOptions.MaxUploadBytes)
        {
            var limitMb = _analysisOptions.MaxUploadBytes / (1024 * 1024);
            throw new UnsupportedFileException($"Files must be {limitMb} MB or smaller.");
        }

        var fileName = command.OriginalFileName ?? string.Empty;
        var extension = Path.GetExtension(fileName);
        if (!string.IsNullOrEmpty(extension) && !AllowedExtensions.Contains(extension))
        {
            throw new UnsupportedFileException(
                $"File extension '{extension}' is not supported. Accepted formats: .csv, .tsv, .txt");
        }

        if (!string.IsNullOrEmpty(command.ContentType) &&
            !AllowedContentTypes.Contains(command.ContentType))
        {
            throw new UnsupportedFileException(
                $"Content type '{command.ContentType}' is not supported.");
        }

        // Sniff rather than trust: a CSV extension says nothing about the bytes.
        Span<byte> head = stackalloc byte[Math.Min(8192, command.Content.Length)];
        command.Content.AsSpan(0, head.Length).CopyTo(head);
        if (head.IndexOf((byte)0x00) >= 0)
        {
            throw new UnsupportedFileException(
                "The uploaded file appears to be binary. Datara currently accepts CSV and TSV files.");
        }

        if (command.TargetColumn is { Length: > 200 })
        {
            throw new ValidationFailedException("Target column name is too long.");
        }
    }

    private static string ResolveName(UploadDatasetCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.Name))
        {
            var trimmed = command.Name.Trim();
            return trimmed.Length > 200 ? trimmed[..200] : trimmed;
        }

        var fileName = command.OriginalFileName ?? string.Empty;
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        if (!string.IsNullOrWhiteSpace(withoutExtension))
        {
            return withoutExtension.Length > 200 ? withoutExtension[..200] : withoutExtension;
        }

        return "Untitled dataset";
    }

    /// <summary>
    /// Keeps a display-only filename free of path traversal and control characters.
    /// The value is never used to construct a storage path.
    /// </summary>
    internal static string SanitiseFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "upload.csv";
        }

        var leaf = Path.GetFileName(fileName.Replace('\\', '/'));
        var cleaned = new string(leaf
            .Where(c => !char.IsControl(c) && c != '\0')
            .Select(c => c is '<' or '>' or ':' or '"' or '|' or '?' or '*' ? '_' : c)
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? "upload.csv" : cleaned[..Math.Min(255, cleaned.Length)];
    }

    private async Task<Dataset> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var dataset = await _repository.GetByIdAsync(id, cancellationToken);
        return dataset ?? throw new NotFoundException("Dataset", id);
    }

    private async Task EnsureExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await _repository.ExistsAsync(id, cancellationToken))
        {
            throw new NotFoundException("Dataset", id);
        }
    }

    private Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _repository.SaveChangesAsync(cancellationToken);

    private static DatasetListItemDto ToListItem(Dataset dataset) => new()
    {
        Id = dataset.Id,
        Name = dataset.Name,
        OriginalFileName = dataset.OriginalFileName,
        Status = EnumText.Of(dataset.Status),
        FileSizeBytes = dataset.FileSizeBytes,
        RowCount = dataset.RowCount,
        ColumnCount = dataset.ColumnCount,
        MlReadinessScore = dataset.MlReadinessScore,
        DataQualityScore = dataset.DataQualityScore,
        UploadedAt = dataset.UploadedAt,
    };

    private static DatasetDetailDto ToDetail(Dataset dataset, int issueCount, int criticalCount) => new()
    {
        Id = dataset.Id,
        Name = dataset.Name,
        OriginalFileName = dataset.OriginalFileName,
        ContentType = dataset.ContentType,
        Format = dataset.Format,
        FileSizeBytes = dataset.FileSizeBytes,
        RowCount = dataset.RowCount,
        ColumnCount = dataset.ColumnCount,
        Status = EnumText.Of(dataset.Status),
        MlReadinessScore = dataset.MlReadinessScore,
        DataQualityScore = dataset.DataQualityScore,
        Description = dataset.Description,
        TargetColumn = dataset.TargetColumn,
        UploadedAt = dataset.UploadedAt,
        AnalyzedAt = dataset.AnalyzedAt,
        IssueCount = issueCount,
        CriticalIssueCount = criticalCount,
    };

    private static DatasetOverviewDto PendingOverview(Dataset dataset) => new()
    {
        DatasetId = dataset.Id,
        Name = dataset.Name,
        Status = EnumText.Of(dataset.Status),
        Statistics = new DatasetStatisticsDto
        {
            RowCount = dataset.RowCount,
            ColumnCount = dataset.ColumnCount,
        },
    };

    private static DatasetStatisticsDto BuildStatistics(
        Dataset dataset,
        DatasetAnalysis analysis,
        IReadOnlyList<DatasetColumn> columns) => new()
    {
        RowCount = analysis.RowCount,
        ColumnCount = analysis.ColumnCount,
        TotalCells = analysis.TotalCells,
        TotalMissing = analysis.TotalMissing,
        OverallMissingPercentage = analysis.OverallMissingPercentage,
        DuplicateRowCount = analysis.DuplicateRowCount,
        DuplicatePercentage = analysis.DuplicatePercentage,
        FullyEmptyColumns = analysis.FullyEmptyColumns,
        MemoryUsageBytes = analysis.MemoryUsageBytes,
        NumericColumnCount = columns.Count(c =>
            c.InferredKind is ColumnKind.Numeric or ColumnKind.Integer),
        CategoricalColumnCount = columns.Count(c => c.InferredKind == ColumnKind.Categorical),
        TextColumnCount = columns.Count(c => c.InferredKind == ColumnKind.Text),
        ConstantColumnCount = columns.Count(c => c.IsConstant),
    };

    // Compare against the same rendering the DTOs use. Matching on nameof() instead
    // silently yields an all-zero breakdown the moment the casing differs.
    private static IssueSeverityBreakdownDto Breakdown(IReadOnlyList<QualityIssueDto> issues) => new()
    {
        Critical = issues.Count(i => i.Severity == EnumText.Of(IssueSeverity.Critical)),
        High = issues.Count(i => i.Severity == EnumText.Of(IssueSeverity.High)),
        Medium = issues.Count(i => i.Severity == EnumText.Of(IssueSeverity.Medium)),
        Low = issues.Count(i => i.Severity == EnumText.Of(IssueSeverity.Low)),
        Info = issues.Count(i => i.Severity == EnumText.Of(IssueSeverity.Info)),
    };

    private static QualityIssueDto ToIssueDto(Domain.Entities.DataQualityIssue issue) => new()
    {
        Id = issue.Id,
        Code = issue.Code,
        Severity = EnumText.Of(issue.Severity),
        Category = EnumText.Of(issue.Category),
        Column = issue.Column,
        Title = issue.Title,
        Description = issue.Description,
        Evidence = ParseEvidence(issue.EvidenceJson),
        Recommendation = issue.Recommendation,
        ScoreImpact = issue.ScoreImpact,
    };

    private static AnomalyDto ToAnomalyDto(Domain.Entities.AnomalyRecord anomaly) => new()
    {
        Column = anomaly.Column,
        Kind = anomaly.Kind,
        Description = anomaly.Description,
        Severity = anomaly.Severity,
    };

    private static Dictionary<string, object?> ParseEvidence(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, object?>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) is { } parsed
                ? parsed.ToDictionary(kv => kv.Key, kv => (object?)kv.Value)
                : new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            // Evidence is diagnostic, not contractual: never fail a read over it.
            return new Dictionary<string, object?> { ["raw"] = json };
        }
    }
}