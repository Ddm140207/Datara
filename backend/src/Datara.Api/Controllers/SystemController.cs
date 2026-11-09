using Datara.Api.Infrastructure;
using Datara.Application.Ai;
using Datara.Application.Ai.Abstractions;
using Datara.Application.Analysis;
using Datara.Application.Common;

namespace Datara.Api.Controllers;

[ApiController]
[Route(ApiRoutes.Ai.Capabilities)]
[Produces("application/json")]
[Tags("Datara AI")]
public sealed class AiCapabilitiesController : ControllerBase
{
    private readonly IDataraAiService _ai;
    private readonly IAnalysisServiceClient _analysis;

    public AiCapabilitiesController(IDataraAiService ai, IAnalysisServiceClient analysis)
    {
        _ai = ai;
        _analysis = analysis;
    }

    /// <summary>Reports whether a language model is configured, without exposing secrets.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(AiCapabilitiesDto), StatusCodes.Status200OK)]
    public ActionResult<AiCapabilitiesDto> Get() => Ok(_ai.GetCapabilities());
}

/// <summary>Dependency health for the API and its downstream services.</summary>
[ApiController]
[Route(ApiRoutes.System.Health)]
[Produces("application/json")]
[Tags("System")]
public sealed class HealthController : ControllerBase
{
    private readonly IAnalysisServiceClient _analysis;

    public HealthController(IAnalysisServiceClient analysis)
    {
        _analysis = analysis;
    }

    [HttpGet]
    [ProducesResponseType(typeof(HealthDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<HealthDto>> Get(CancellationToken cancellationToken)
    {
        var analysisHealthy = await _analysis.IsHealthyAsync(cancellationToken);

        return Ok(new HealthDto
        {
            Status = analysisHealthy ? "healthy" : "degraded",
            Service = "datara-api",
            Version = typeof(HealthController).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Dependencies = new Dictionary<string, string>
            {
                ["analysis-service"] = analysisHealthy ? "healthy" : "unreachable",
                ["database"] = "configured",
                ["file-storage"] = "configured",
            },
        });
    }
}

public sealed record HealthDto
{
    public string Status { get; init; } = string.Empty;

    public string Service { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public Dictionary<string, string> Dependencies { get; init; } = new();
}
