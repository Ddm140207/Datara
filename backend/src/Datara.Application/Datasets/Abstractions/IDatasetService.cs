using Datara.Application.Datasets.Dtos;
using Datara.Domain.Entities;

namespace Datara.Application.Datasets.Abstractions;

/// <summary>Use cases for dataset ingestion, analysis and querying.</summary>
public interface IDatasetService
{
    Task<UploadDatasetResponse> UploadAsync(
        UploadDatasetCommand command,
        Guid ownerId,
        CancellationToken cancellationToken);

    Task<PagedResult<DatasetListItemDto>> ListAsync(
        string? query,
        Guid ownerId,
        PageOptions page,
        CancellationToken cancellationToken);

    Task<DatasetDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<DatasetOverviewDto> GetOverviewAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ColumnDto>> GetColumnsAsync(Guid id, CancellationToken cancellationToken);

    Task<ColumnDto> GetColumnAsync(Guid id, string columnName, CancellationToken cancellationToken);

    Task<PagedResult<QualityIssueDto>> GetIssuesAsync(
        Guid id,
        string? severity,
        string? category,
        string? column,
        PageOptions page,
        CancellationToken cancellationToken);

    Task<CorrelationMatrixDto?> GetCorrelationsAsync(Guid id, CancellationToken cancellationToken);

    Task<SampleRowsDto?> GetSamplesAsync(Guid id, int limit, CancellationToken cancellationToken);

    Task<DatasetDetailDto> ReanalyseAsync(
        Guid id,
        AnalyseDatasetCommand command,
        CancellationToken cancellationToken);

    Task<DatasetDetailDto> UpdateAsync(
        Guid id,
        RenameDatasetRequest request,
        CancellationToken cancellationToken);

    Task<DeleteDatasetResult> DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<AnalysisRunDto>> GetRunsAsync(Guid id, CancellationToken cancellationToken);
}