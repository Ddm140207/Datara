using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Datara.Application.Common;
using Datara.Application.Datasets.Dtos;
using Microsoft.Extensions.DependencyInjection;

namespace Datara.IntegrationTests;

/// <summary>
/// One shared database and host for the whole suite. Tests stay independent by using
/// unique dataset names and asserting only on rows they created themselves.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DataraCollection : ICollectionFixture<DataraApiFactory>
{
    public const string Name = "datara-api";
}

[Collection(DataraCollection.Name)]
public sealed class UploadPipelineTests
{
    private readonly DataraApiFactory _factory;
    private readonly HttpClient _client;

    public UploadPipelineTests(DataraApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_reports_healthy_while_analysis_service_is_reachable()
    {
        var health = await GetAsync<HealthResponse>(ApiRoutes.System.Health);

        Assert.Equal("healthy", health.Status);
        Assert.Equal("healthy", health.Dependencies["analysis-service"]);
    }

    [Fact]
    public async Task Upload_persists_profile_and_returns_scores()
    {
        var upload = await UploadAsync();

        Assert.Equal("customer_churn", upload.Dataset.Name);
        Assert.True(upload.AnalysisCompleted);

        var overview = await GetAsync<DatasetOverviewDto>(Overview(upload.Dataset.Id));

        Assert.Equal(4, overview.Statistics.RowCount);
        Assert.Equal(5, overview.Statistics.ColumnCount);
        Assert.Equal(15.00m, overview.Statistics.OverallMissingPercentage);
        Assert.Equal(1, overview.Statistics.DuplicateRowCount);

        Assert.Equal(58.25m, overview.Readiness.Score);
        Assert.Equal("C", overview.Readiness.Grade);
        Assert.Equal(61.00m, overview.Readiness.DataQualityScore);

        // Both stub issues survived the jsonb round-trip and the severity split is right.
        Assert.Equal(2, overview.Issues.Count);
        Assert.Equal(1, overview.IssueBreakdown.High);
        Assert.Equal(1, overview.IssueBreakdown.Medium);

        Assert.Equal(2, overview.Readiness.Components.Count);
    }

    [Fact]
    public async Task Columns_endpoint_preserves_nested_jsonb_and_nulls()
    {
        var id = (await UploadAsync()).Dataset.Id;
        var columns = await GetAsync<List<ColumnDto>>($"/api/v1/datasets/{id}/columns");
        var byName = columns.ToDictionary(c => c.Name, c => c);

        Assert.Equal(5, columns.Count);

        // jsonb list-of-objects survived intact.
        var plan = byName["plan"];
        Assert.Equal(2, plan.TopCategories.Count);
        Assert.Equal("pro", plan.TopCategories[0].Value);
        Assert.Equal(66.67m, plan.TopCategories[0].Percentage);

        // A categorical column reports null numeric stats, not zeros.
        Assert.Null(plan.Numeric);

        // Datetime bounds survived.
        Assert.Equal(2024, byName["signed_up_at"].DateMin?.Year);

        // Constant-column detection and decimal statistics survived.
        Assert.True(byName["region"].IsConstant);
        Assert.Equal(42.5m, byName["monthly_spend"].Numeric!.Mean);
        Assert.Equal(12.25m, byName["monthly_spend"].Numeric!.StdDev);

        // Histogram and outlier window round-tripped.
        Assert.Equal(2, byName["monthly_spend"].Histogram!.Counts.Count);
        Assert.Equal("iqr", byName["monthly_spend"].Outliers[0].Method);
    }

    [Fact]
    public async Task Correlations_endpoint_returns_the_stored_matrix_with_nulls_intact()
    {
        var id = (await UploadAsync()).Dataset.Id;
        var correlations = await GetAsync<CorrelationMatrixDto>($"/api/v1/datasets/{id}/correlations");

        Assert.Equal("pearson", correlations.Method);
        Assert.Equal(3, correlations.Columns.Count);

        var pair = Assert.Single(correlations.Pairs);
        Assert.Equal("monthly_spend", pair.ColumnA);
        Assert.Equal(-0.42m, pair.Coefficient);

        // A cell that was null must stay null rather than silently become zero.
        Assert.Null(correlations.Values[0][2]);
        Assert.Equal(1.0m, correlations.Values[0][0]);
        Assert.Equal(-0.42m, correlations.Values[0][1]);
    }

    [Fact]
    public async Task Samples_endpoint_decodes_stored_rows()
    {
        var id = (await UploadAsync()).Dataset.Id;
        var samples = await GetAsync<SampleRowsDto>($"/api/v1/datasets/{id}/samples");

        Assert.Equal(5, samples.Columns.Count);
        Assert.Equal("C-1", samples.Rows[0][0]);
        Assert.Equal("22.5", samples.Rows[0][1]);
        Assert.Equal("2024-01-01", samples.Rows[0][4]);
    }

    [Fact]
    public async Task Issues_endpoint_filters_by_severity_and_column()
    {
        var id = (await UploadAsync()).Dataset.Id;

        var high = await GetAsync<PagedResult<QualityIssueDto>>(
            $"/api/v1/datasets/{id}/issues?severity=high");

        var issue = Assert.Single(high.Items);
        Assert.Equal("high_missingness", issue.Code);
        Assert.Equal("monthly_spend", issue.Column);
        Assert.Equal(-12.5m, issue.ScoreImpact);

        // Evidence is a jsonb object, so it must arrive structured rather than as a
        // flattened string. object? deserialises to JsonElement.
        var evidence = Assert.IsType<JsonElement>(issue.Evidence["missing_percentage"]);
        Assert.Equal(25.0d, evidence.GetDouble());

        var none = await GetAsync<PagedResult<QualityIssueDto>>(
            $"/api/v1/datasets/{id}/issues?severity=critical");

        Assert.Empty(none.Items);
    }

    [Fact]
    public async Task Runs_endpoint_records_the_profiling_attempt()
    {
        var id = (await UploadAsync()).Dataset.Id;
        var runs = await GetAsync<List<AnalysisRunDto>>($"/api/v1/datasets/{id}/runs");

        var run = Assert.Single(runs);
        Assert.Equal("succeeded", run.Status);
        Assert.Equal("upload", run.Trigger);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public async Task List_search_finds_a_dataset_by_name()
    {
        var marker = $"marker{Guid.NewGuid():N}";
        var id = (await UploadAsync(name: $"churn {marker}")).Dataset.Id;

        var page = await GetAsync<PagedResult<DatasetListItemDto>>($"/api/v1/datasets?query={marker}");

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(id, page.Items[0].Id);
        Assert.Equal(58.25m, page.Items[0].MlReadinessScore);
    }

    [Fact]
    public async Task Reanalyse_replaces_child_rows_instead_of_appending()
    {
        var id = (await UploadAsync()).Dataset.Id;

        var before = await GetAsync<List<ColumnDto>>($"/api/v1/datasets/{id}/columns");
        var issuesBefore = await GetAsync<PagedResult<QualityIssueDto>>($"/api/v1/datasets/{id}/issues");

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/datasets/{id}/analyse",
            new AnalyseDatasetCommand());
        response.EnsureSuccessStatusCode();

        var after = await GetAsync<List<ColumnDto>>($"/api/v1/datasets/{id}/columns");
        var issuesAfter = await GetAsync<PagedResult<QualityIssueDto>>($"/api/v1/datasets/{id}/issues");

        // The unique index on (dataset_id, name) would blow up if rows were appended.
        Assert.Equal(before.Count, after.Count);
        Assert.Equal(issuesBefore.TotalCount, issuesAfter.TotalCount);
    }

    [Fact]
    public async Task Rename_and_delete_round_trip()
    {
        var id = (await UploadAsync()).Dataset.Id;
        var renamed = $"renamed-{Guid.NewGuid():N}";

        var updated = await PatchAsync<DatasetDetailDto>(
            $"/api/v1/datasets/{id}",
            new RenameDatasetRequest { Name = renamed, Description = "updated" });

        Assert.Equal(renamed, updated.Name);
        Assert.Equal("updated", updated.Description);

        var deleted = await _client.DeleteAsync($"/api/v1/datasets/{id}");
        deleted.EnsureSuccessStatusCode();
        var deleteResult = await deleted.Content.ReadFromJsonAsync<DeleteDatasetResult>();
        Assert.NotNull(deleteResult);
        Assert.True(deleteResult.Deleted);

        var gone = await _client.GetAsync($"/api/v1/datasets/{id}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Dataset_detail_reports_stored_file_metadata()
    {
        var id = (await UploadAsync()).Dataset.Id;
        var detail = await GetAsync<DatasetDetailDto>($"/api/v1/datasets/{id}");

        Assert.Equal("customer_churn.csv", detail.OriginalFileName);
        Assert.Equal("csv", detail.Format);
        Assert.True(detail.FileSizeBytes > 0);
        Assert.Equal(2, detail.IssueCount);
        Assert.NotNull(detail.AnalyzedAt);
    }

    [Fact]
    public async Task Unknown_dataset_returns_404_problem_details()
    {
        var response = await _client.GetAsync($"/api/v1/datasets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("problem+json", response.Content.Headers.ContentType!.MediaType!);
    }

    [Theory]
    [InlineData("", "empty.csv")]
    [InlineData("id,name\na,b\n", "data.xlsx")]
    [InlineData("id,name\na,b\n", "archive.zip")]
    public async Task Invalid_uploads_are_rejected_with_422(string body, string fileName)
    {
        using var content = new MultipartFormDataContent();
        content.Add(
            new ByteArrayContent(Encoding.UTF8.GetBytes(body)),
            "file",
            fileName);

        var response = await _client.PostAsync(ApiRoutes.Datasets.Root, content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Binary_content_is_rejected_even_with_a_csv_extension()
    {
        // The extension is a hint; the bytes are the evidence.
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 }), "file", "sneaky.csv");

        var response = await _client.PostAsync(ApiRoutes.Datasets.Root, content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Extensionless_tabular_file_is_accepted_by_content()
    {
        // No extension is not a rejection: the delimiter is sniffed downstream.
        var upload = await UploadAsync(fileName: "no-extension");

        Assert.True(upload.AnalysisCompleted);
    }

    [Fact]
    public void Every_controller_dependency_resolves_from_the_container()
    {
        // A missing registration would otherwise only surface at request time.
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider;

        Assert.NotNull(provider.GetService<Application.Datasets.Abstractions.IDatasetService>());
        Assert.NotNull(provider.GetService<Application.Datasets.Abstractions.IDatasetRepository>());
        Assert.NotNull(provider.GetService<Application.Ai.Abstractions.IDataraAiService>());
        Assert.NotNull(provider.GetService<Application.Ai.IDatasetContextBuilder>());
        Assert.NotNull(provider.GetService<Application.Ai.ILlmProvider>());
        Assert.NotNull(provider.GetService<Application.Ai.IDeterministicAiFallback>());
        Assert.NotNull(provider.GetService<Application.Analysis.IAnalysisServiceClient>());
        Assert.NotNull(provider.GetService<Application.Common.IFileStorage>());
        Assert.NotNull(provider.GetService<Infrastructure.Persistence.DataraDbContext>());
        Assert.NotNull(provider.GetService<Datara.Api.Controllers.OwnerContext>());
    }

    [Fact]
    public async Task Traversal_filename_cannot_escape_the_storage_root()
    {
        var response = await _client.PostAsync(
            ApiRoutes.Datasets.Root,
            Multipart("../../../etc/passwd", Csv()));

        response.EnsureSuccessStatusCode();

        var uploaded = await response.Content.ReadFromJsonAsync<UploadDatasetResponse>();
        Assert.NotNull(uploaded);
        var id = uploaded.Dataset.Id;

        using var scope = _factory.Services.CreateScope();
        var dataset = scope.ServiceProvider
            .GetRequiredService<Infrastructure.Persistence.DataraDbContext>()
            .Datasets.Single(d => d.Id == id);

        // The key is server-generated, so the traversal attempt is inert.
        Assert.DoesNotContain("..", dataset.StorageKey);
        Assert.DoesNotContain("passwd", dataset.StorageKey);

        var storage = scope.ServiceProvider.GetRequiredService<Application.Common.IFileStorage>();
        Assert.True(await storage.ExistsAsync(dataset.StorageKey, default));
    }

    [Fact]
    public async Task Path_traversal_storage_key_is_rejected_on_read()
    {
        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<Application.Common.IFileStorage>();

        // Defence in depth: even a poisoned key cannot read outside the root.
        await Assert.ThrowsAnyAsync<Exception>(
            () => storage.ReadAsync("../../secrets.csv", default));

        Assert.Null(await storage.ReadAsync("datasets/missing.csv", default));
    }

    private static string Overview(Guid id) => $"/api/v1/datasets/{id}/overview";

    private static string Csv() =>
        "customer_id,monthly_spend,plan,region,signed_up_at\n"
        + "C-1,22.5,pro,EU,2024-01-01\n"
        + "C-2,40.0,free,EU,2024-02-01\n"
        + "C-3,,pro,EU,2024-03-01\n"
        + "C-3,65.0,pro,EU,2024-03-01\n";

    private static MultipartFormDataContent Multipart(string fileName, string body)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", fileName);
        return content;
    }

    private async Task<UploadDatasetResponse> UploadAsync(string? name = null, string fileName = "customer_churn.csv")
    {
        using var content = Multipart(fileName, Csv());

        if (name is not null)
        {
            content.Add(new StringContent(name), "name");
        }

        var response = await _client.PostAsync(ApiRoutes.Datasets.Root, content);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<UploadDatasetResponse>())!;
    }

    private async Task<T> GetAsync<T>(string url)
    {
        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<T> PatchAsync<T>(string url, object body)
    {
        var response = await _client.PatchAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed record HealthResponse(
        string Status,
        string Service,
        Dictionary<string, string> Dependencies);
}
