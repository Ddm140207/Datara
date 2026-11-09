using System.Net;
using System.Text;
using System.Text.Json;
using Datara.Application.Analysis;

namespace Datara.IntegrationTests;

/// <summary>
/// Intercepts every HTTP call to the analysis service and returns a canned profile.
/// A real socket is never opened, so the suite cannot accidentally depend on a
/// running Python process.
/// </summary>
internal sealed class StubAnalysisHandler : HttpMessageHandler
{
    private readonly DatasetProfileResponse _profile;

    public StubAnalysisHandler(DatasetProfileResponse profile)
    {
        _profile = profile;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath.Contains("/health", StringComparison.Ordinal) == true)
        {
            return Task.FromResult(Json(HttpStatusCode.OK, new { status = "healthy" }));
        }

        if (request.RequestUri?.AbsolutePath.Contains("/profile", StringComparison.Ordinal) != true)
        {
            return Task.FromResult(Json(HttpStatusCode.NotFound, new { error = new { code = "not_found" } }));
        }

        return Task.FromResult(Json(HttpStatusCode.OK, _profile));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object payload) => new(status)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json"),
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
