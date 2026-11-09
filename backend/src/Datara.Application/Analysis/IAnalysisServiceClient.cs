namespace Datara.Application.Analysis;

/// <summary>
/// Client for the Python analysis service. The .NET API orchestrates; pandas does
/// the data science. This is the single seam between the two.
/// </summary>
public interface IAnalysisServiceClient
{
    /// <summary>Profiles an in-memory CSV payload.</summary>
    /// <exception cref="Domain.Exceptions.UnsupportedFileException">
    /// The payload is not readable delimited text.
    /// </exception>
    /// <exception cref="Domain.Exceptions.AnalysisFailedException">
    /// Profiling failed for a data reason.
    /// </exception>
    /// <exception cref="Domain.Exceptions.AnalysisServiceUnavailableException">
    /// The service could not be reached or returned a 5xx.
    /// </exception>
    Task<DatasetProfileResponse> ProfileAsync(
        byte[] content,
        string fileName,
        bool includeSamples = true,
        bool includeCorrelations = true,
        bool detectOutliers = true,
        string? targetColumn = null,
        CancellationToken cancellationToken = default);

    /// <summary>Cheap liveness probe for the dependency health endpoint.</summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}