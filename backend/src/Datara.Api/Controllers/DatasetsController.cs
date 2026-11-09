using Datara.Application.Common;
using Datara.Application.Datasets.Abstractions;
using Datara.Application.Datasets.Dtos;
using Datara.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Datara.Api.Controllers;

[ApiController]
[Route(ApiRoutes.Datasets.Root)]
[Produces("application/json")]
[Tags("Datasets")]
public sealed class DatasetsController : ControllerBase
{
    private readonly IDatasetService _datasets;
    private readonly OwnerContext _owner;

    public DatasetsController(IDatasetService datasets, OwnerContext owner)
    {
        _datasets = datasets;
        _owner = owner;
    }

    /// <summary>Lists datasets for the current owner, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<DatasetListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DatasetListItemDto>>> List(
        [FromQuery] string? query,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageOptions.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _datasets.ListAsync(
            query,
            _owner.OwnerId,
            new PageOptions(page, pageSize),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Uploads a CSV or TSV file and profiles it.
    /// </summary>
    /// <remarks>
    /// Profiling is synchronous so the response already carries scores on first
    /// load. Runs are recorded either way, and a failure leaves the dataset in
    /// <c>Failed</c> with the reason available from the runs endpoint rather than
    /// rejecting the upload.
    /// </remarks>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UploadDatasetResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UploadDatasetResponse>> Upload(
        IFormFile file,
        [FromForm] string? name,
        [FromForm] string? targetColumn,
        [FromForm] string? description,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            throw new ValidationFailedException("The uploaded file is empty.");
        }

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        var response = await _datasets.UploadAsync(
            new UploadDatasetCommand
            {
                OriginalFileName = file.FileName,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "text/csv" : file.ContentType,
                Content = buffer.ToArray(),
                Name = name,
                TargetColumn = targetColumn,
                Description = description,
            },
            _owner.OwnerId,
            cancellationToken);

        return Created(
            $"/{ApiRoutes.Datasets.ById.Replace("{id:guid}", response.Dataset.Id.ToString())}",
            response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DatasetDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DatasetDetailDto>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await _datasets.GetByIdAsync(id, cancellationToken));

    /// <summary>Everything the dashboard needs for the detail page, in one round trip.</summary>
    [HttpGet("{id:guid}/overview")]
    [ProducesResponseType(typeof(DatasetOverviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DatasetOverviewDto>> Overview(Guid id, CancellationToken cancellationToken) =>
        Ok(await _datasets.GetOverviewAsync(id, cancellationToken));

    [HttpGet("{id:guid}/columns")]
    [ProducesResponseType(typeof(IReadOnlyList<ColumnDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ColumnDto>>> Columns(Guid id, CancellationToken cancellationToken) =>
        Ok(await _datasets.GetColumnsAsync(id, cancellationToken));

    [HttpGet("{id:guid}/columns/{column}")]
    [ProducesResponseType(typeof(ColumnDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ColumnDto>> Column(
        Guid id,
        string column,
        CancellationToken cancellationToken) =>
        Ok(await _datasets.GetColumnAsync(id, column, cancellationToken));

    /// <summary>Issues with optional severity, category and column filters.</summary>
    [HttpGet("{id:guid}/issues")]
    [ProducesResponseType(typeof(PagedResult<QualityIssueDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<QualityIssueDto>>> Issues(
        Guid id,
        [FromQuery] string? severity,
        [FromQuery] string? category,
        [FromQuery] string? column,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageOptions.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _datasets.GetIssuesAsync(
            id,
            severity,
            category,
            column,
            new PageOptions(page, pageSize),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("{id:guid}/correlations")]
    [ProducesResponseType(typeof(CorrelationMatrixDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CorrelationMatrixDto?>> Correlations(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _datasets.GetCorrelationsAsync(id, cancellationToken));

    /// <summary>Sample rows captured at analysis time, capped server-side.</summary>
    [HttpGet("{id:guid}/samples")]
    [ProducesResponseType(typeof(SampleRowsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SampleRowsDto?>> Samples(
        Guid id,
        [FromQuery] int limit = 25,
        CancellationToken cancellationToken = default) =>
        Ok(await _datasets.GetSamplesAsync(id, limit, cancellationToken));

    /// <summary>Re-runs profiling against the stored file.</summary>
    [HttpPost("{id:guid}/analyse")]
    [ProducesResponseType(typeof(DatasetDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DatasetDetailDto>> Analyse(
        Guid id,
        [FromBody] AnalyseDatasetCommand? command,
        CancellationToken cancellationToken) =>
        Ok(await _datasets.ReanalyseAsync(id, command ?? new AnalyseDatasetCommand(), cancellationToken));

    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(DatasetDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DatasetDetailDto>> Update(
        Guid id,
        [FromBody] RenameDatasetRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _datasets.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(DeleteDatasetResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<DeleteDatasetResult>> Delete(Guid id, CancellationToken cancellationToken) =>
        Ok(await _datasets.DeleteAsync(id, cancellationToken));

    /// <summary>Audit trail of analysis attempts, successful and failed.</summary>
    [HttpGet("{id:guid}/runs")]
    [ProducesResponseType(typeof(IReadOnlyList<AnalysisRunDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AnalysisRunDto>>> Runs(Guid id, CancellationToken cancellationToken) =>
        Ok(await _datasets.GetRunsAsync(id, cancellationToken));
}
