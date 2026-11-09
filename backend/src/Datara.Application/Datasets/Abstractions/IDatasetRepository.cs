namespace Datara.Application.Datasets.Abstractions;

/// <summary>Persists dataset metadata and analysis results.</summary>
public interface IDatasetRepository
{
    Task<Dataset?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Loads the most recent completed analysis for a dataset.</summary>
    Task<DatasetAnalysis?> GetLatestAnalysisAsync(Guid datasetId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DatasetAnalysis>> GetAnalysisHistoryAsync(Guid datasetId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DatasetColumn>> GetColumnsAsync(Guid datasetId, CancellationToken cancellationToken);

    Task<DatasetColumn?> GetColumnAsync(Guid datasetId, string columnName, CancellationToken cancellationToken);

    Task<IReadOnlyList<DataQualityIssue>> GetIssuesAsync(Guid datasetId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AnalysisRun>> GetRunsAsync(Guid datasetId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Dataset>> SearchAsync(string? query, Guid ownerId, int skip, int take, CancellationToken cancellationToken);

    Task<int> CountAsync(string? query, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Tracks a new dataset for insertion.</summary>
    Task AddAsync(Dataset dataset, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the persisted analysis for a dataset in a single transaction:
    /// new columns, new issues, the new analysis record, and the dataset's
    /// headline scores. Previous history is retained.
    /// </summary>
    /// <remarks>
    /// The caller supplies the mapped columns and issues because translating the
    /// analysis-service payload is the application layer's job, not the
    /// repository's. The implementation owns ordering and transaction scope.
    /// </remarks>
    Task SaveAnalysisAsync(
        Dataset dataset,
        DatasetAnalysis analysis,
        IReadOnlyList<DatasetColumn> columns,
        IReadOnlyList<DataQualityIssue> issues,
        CancellationToken cancellationToken);

    /// <summary>Tracks a new or modified analysis run for persistence.</summary>
    Task AddRunAsync(AnalysisRun run, CancellationToken cancellationToken);

    Task RemoveAsync(Dataset dataset, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Commits pending changes in the ambient unit of work. Keeps write concerns in
    /// the infrastructure layer and out of the service contract.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}