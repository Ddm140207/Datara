using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Datara.Application.Analysis;
using Datara.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Datara.Infrastructure.Analysis;

/// <summary>
/// HTTP client for the Python analysis service.
///
/// Transport-level failures surface as
/// <see cref="AnalysisServiceUnavailableException"/> so the application layer can
/// record a failed run instead of leaving a dataset stuck. A 4xx from the service
/// is a data problem, not an outage, so it maps to
/// <see cref="UnsupportedFileException"/>.
/// </summary>
public sealed class HttpAnalysisServiceClient : IAnalysisServiceClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AnalysisServiceOptions _options;
    private readonly ILogger<HttpAnalysisServiceClient> _logger;

    /// <remarks>
    /// The client is resolved per call from the factory rather than injected as a bare
    /// <see cref="HttpClient"/>. A bare HttpClient is registered transiently with no
    /// BaseAddress, so injecting one silently drops both the configured origin and any
    /// message-handler configuration attached to the named client.
    /// </remarks>
    public HttpAnalysisServiceClient(
        IHttpClientFactory httpClientFactory,
        IOptions<AnalysisServiceOptions> options,
        ILogger<HttpAnalysisServiceClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DatasetProfileResponse> ProfileAsync(
        byte[] content,
        string fileName,
        bool includeSamples = true,
        bool includeCorrelations = true,
        bool detectOutliers = true,
        string? targetColumn = null,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        var payload = new ByteArrayContent(content);

        // The analysis service sniffs content rather than trusting metadata, but the
        // filename still helps it choose a fallback encoding.
        payload.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(payload, "file", string.IsNullOrWhiteSpace(fileName) ? "upload.csv" : fileName);
        form.Add(new StringContent(Bool(includeSamples)), "include_samples");
        form.Add(new StringContent(Bool(includeCorrelations)), "include_correlations");
        form.Add(new StringContent(Bool(detectOutliers)), "detect_outliers");

        if (!string.IsNullOrWhiteSpace(targetColumn))
        {
            form.Add(new StringContent(targetColumn, Encoding.UTF8), "target_column");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/profile")
        {
            Content = form,
        };

        // Profiling is CPU bound and can legitimately take minutes on a large file,
        // so the timeout comes from configuration rather than HttpClient's default.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);

        using var http = _httpClientFactory.CreateClient(ServiceCollectionExtensions.HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AnalysisServiceUnavailableException(
                $"The analysis service did not respond within {_options.Timeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Could not reach the analysis service at {BaseUrl}", _options.BaseUrl);
            throw new AnalysisServiceUnavailableException(
                "The analysis service is unreachable. Please try again in a moment.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await SafeReadAsync(response, cancellationToken);

                if (response.StatusCode is HttpStatusCode.RequestEntityTooLarge)
                {
                    throw new UnsupportedFileException(
                        "The file exceeds the analysis service size limit.");
                }

                if ((int)response.StatusCode is >= 400 and < 500)
                {
                    _logger.LogWarning(
                        "Analysis service rejected the payload with {StatusCode}: {Body}",
                        response.StatusCode,
                        body);

                    throw new UnsupportedFileException(
                        ExtractDetail(body) ?? "The analysis service could not read this file.");
                }

                _logger.LogError(
                    "Analysis service returned {StatusCode}: {Body}",
                    response.StatusCode,
                    body);

                throw new AnalysisServiceUnavailableException(
                    "The analysis service reported an internal error while profiling the dataset.");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            try
            {
                return JsonSerializer.Deserialize<DatasetProfileResponse>(json, SerializerOptions)
                       ?? throw new AnalysisFailedException(
                           "The analysis service returned an empty profile.");
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Could not deserialise the analysis service response");
                throw new AnalysisServiceUnavailableException(
                    "The analysis service returned a response that could not be read.");
            }
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            using var http = _httpClientFactory.CreateClient(ServiceCollectionExtensions.HttpClientName);
            using var response = await http.GetAsync("health", timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static async Task<string> SafeReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>Pulls FastAPI's <c>detail</c> or <c>error.message</c> field out of a body.</summary>
    private static string? ExtractDetail(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString();
            }

            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw body below.
        }

        return body.Length > 500 ? body[..500] : body;
    }
}
