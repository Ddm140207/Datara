namespace Datara.Application.Analysis;

/// <summary>Tuning for outbound calls to the Python analysis service.</summary>
public sealed class AnalysisServiceOptions
{
    public const string SectionName = "AnalysisService";

    /// <summary>Base address of the FastAPI service.</summary>
    public string BaseUrl { get; set; } = "http://localhost:8000";

    /// <summary>Profiling is CPU bound; keep the timeout generous but bounded.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Bytes accepted by the API before it rejects an upload.</summary>
    public long MaxUploadBytes { get; set; } = 200L * 1024 * 1024;
}