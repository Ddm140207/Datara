using Datara.Application.Datasets.Abstractions;
using Datara.Domain.Enums;
using Datara.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Datara.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IDatasetRepository"/>.
///
/// All queries are composed against <see cref="IQueryable"/> so filtering, paging
/// and sorting happen in postgres rather than in memory. Nothing here is allowed to
/// return tracked entities without <c>AsNoTracking</c> for read paths.
/// </summary>
public sealed class DatasetRepository : IDatasetRepository
{
    private readonly DataraDbContext _db;

    public DatasetRepository(DataraDbContext db)
    {
        _db = db;
    }

    public Task<Dataset?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _db.Datasets.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<DatasetAnalysis?> GetLatestAnalysisAsync(Guid datasetId, CancellationToken cancellationToken) =>
        _db.DatasetAnalyses
            .AsNoTracking()
            .Where(a => a.DatasetId == datasetId && a.ErrorMessage == null)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<DatasetAnalysis>> GetAnalysisHistoryAsync(
        Guid datasetId,
        CancellationToken cancellationToken) =>
        await _db.DatasetAnalyses
            .AsNoTracking()
            .Where(a => a.DatasetId == datasetId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DatasetColumn>> GetColumnsAsync(
        Guid datasetId,
        CancellationToken cancellationToken) =>
        await _db.DatasetColumns
            .AsNoTracking()
            .Where(c => c.DatasetId == datasetId)
            .OrderBy(c => c.Ordinal)
            .ToListAsync(cancellationToken);

    public Task<DatasetColumn?> GetColumnAsync(
        Guid datasetId,
        string columnName,
        CancellationToken cancellationToken) =>
        _db.DatasetColumns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.DatasetId == datasetId && c.Name == columnName, cancellationToken);

    public async Task<IReadOnlyList<DataQualityIssue>> GetIssuesAsync(
        Guid datasetId,
        CancellationToken cancellationToken) =>
        await _db.DataQualityIssues
            .AsNoTracking()
            .Where(i => i.DatasetId == datasetId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AnalysisRun>> GetRunsAsync(
        Guid datasetId,
        CancellationToken cancellationToken) =>
        await _db.AnalysisRuns
            .AsNoTracking()
            .Where(r => r.DatasetId == datasetId)
            .OrderByDescending(r => r.StartedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Dataset>> SearchAsync(
        string? query,
        Guid ownerId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var datasets = _db.Datasets
            .AsNoTracking()
            .Where(d => d.OwnerId == ownerId);

        if (!string.IsNullOrWhiteSpace(query))
        {
            // EF.Functions.ILike keeps the search case-insensitive using the
            // database collation, so it stays index-friendly and dialect-correct.
            var pattern = $"%{EscapeLike(query.Trim())}%";
            datasets = datasets.Where(d => EF.Functions.ILike(d.Name, pattern)
                                          || EF.Functions.ILike(d.Description!, pattern)
                                          || EF.Functions.ILike(d.OriginalFileName, pattern));
        }

        return await datasets
            .OrderByDescending(d => d.UploadedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountAsync(string? query, Guid ownerId, CancellationToken cancellationToken)
    {
        var datasets = _db.Datasets.AsNoTracking().Where(d => d.OwnerId == ownerId);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{EscapeLike(query.Trim())}%";
            datasets = datasets.Where(d => EF.Functions.ILike(d.Name, pattern)
                                          || EF.Functions.ILike(d.Description!, pattern)
                                          || EF.Functions.ILike(d.OriginalFileName, pattern));
        }

        return datasets.CountAsync(cancellationToken);
    }

    public async Task AddAsync(Dataset dataset, CancellationToken cancellationToken)
    {
        await _db.Datasets.AddAsync(dataset, cancellationToken);
    }

    public async Task SaveAnalysisAsync(
        Dataset dataset,
        DatasetAnalysis analysis,
        IReadOnlyList<DatasetColumn> columns,
        IReadOnlyList<DataQualityIssue> issues,
        CancellationToken cancellationToken)
    {
        // Replace the current projection atomically: drop the previous columns and
        // issues, then write the new set. History rows stay in dataset_analyses.
        var previousColumns = await _db.DatasetColumns
            .Where(c => c.DatasetId == dataset.Id)
            .ExecuteDeleteAsync(cancellationToken);

        await _db.DataQualityIssues
            .Where(i => i.DatasetId == dataset.Id)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var column in columns)
        {
            column.DatasetId = dataset.Id;
        }

        foreach (var issue in issues)
        {
            issue.DatasetId = dataset.Id;
            issue.DatasetAnalysisId ??= analysis.Id;
        }

        await _db.DatasetAnalyses.AddAsync(analysis, cancellationToken);
        _db.Datasets.Update(dataset);

        if (columns.Count > 0)
        {
            await _db.DatasetColumns.AddRangeAsync(columns, cancellationToken);
        }

        if (issues.Count > 0)
        {
            await _db.DataQualityIssues.AddRangeAsync(issues, cancellationToken);
        }

        await SaveChangesAsync(cancellationToken);
    }

    public async Task AddRunAsync(AnalysisRun run, CancellationToken cancellationToken)
    {
        // The run is frequently mutated in place before and after SaveChanges, so
        // upsert rather than blind-insert to avoid duplicate-key failures.
        var tracked = _db.AnalysisRuns.Local.FirstOrDefault(r => r.Id == run.Id);
        if (tracked is not null)
        {
            _db.Entry(tracked).CurrentValues.SetValues(run);
            return;
        }

        await _db.AnalysisRuns.AddAsync(run, cancellationToken);
    }

    public Task RemoveAsync(Dataset dataset, CancellationToken cancellationToken)
    {
        _db.Datasets.Remove(dataset);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        _db.Datasets.AsNoTracking().AnyAsync(d => d.Id == id, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Escapes LIKE wildcards so a search for "50%" does not match everything.</summary>
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
