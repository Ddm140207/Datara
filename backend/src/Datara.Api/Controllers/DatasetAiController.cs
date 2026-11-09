using Datara.Api.Infrastructure;
using Datara.Application.Ai;
using Datara.Application.Ai.Abstractions;
using Datara.Application.Common;
using Datara.Application.Datasets.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Datara.Api.Controllers;

/// <summary>
/// Datara AI endpoints.
///
/// Both routes need a profile to answer from. The dataset already has a persisted
/// analysis, so the AI layer works from the stored projection rather than
/// re-invoking pandas, which keeps chat latency in milliseconds.
/// </summary>
[ApiController]
[Route(ApiRoutes.Base + "/datasets/{id:guid}/ai")]
[Produces("application/json")]
[Tags("Datara AI")]
public sealed class DatasetAiController : ControllerBase
{
    private readonly IDataraAiService _ai;
    private readonly IDatasetService _datasets;

    public DatasetAiController(IDataraAiService ai, IDatasetService datasets)
    {
        _ai = ai;
        _datasets = datasets;
    }

    /// <summary>Structured summary: calculated facts plus model or rule-based narrative.</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(AiDatasetSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AiDatasetSummary>> Summary(Guid id, CancellationToken cancellationToken)
    {
        var (profile, name) = await LoadProfileAsync(id, cancellationToken);
        return Ok(await _ai.GenerateSummaryAsync(profile, name, cancellationToken));
    }

    /// <summary>Ask a question about the dataset.</summary>
    [HttpPost("chat")]
    [ProducesResponseType(typeof(AiChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AiChatResponse>> Chat(
        Guid id,
        [FromBody] AiChatRequest request,
        CancellationToken cancellationToken)
    {
        var (profile, name) = await LoadProfileAsync(id, cancellationToken);
        return Ok(await _ai.AskAsync(profile, name, request, cancellationToken));
    }

    private async Task<(Datara.Application.Analysis.DatasetProfileResponse Profile, string Name)> LoadProfileAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var detail = await _datasets.GetByIdAsync(id, cancellationToken);
        var overview = await _datasets.GetOverviewAsync(id, cancellationToken);

        var profile = ProfileRehydrator.Rehydrate(detail, overview);
        return (profile, detail.Name);
    }
}
