using Datara.Application.Analysis;
using Datara.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Datara.IntegrationTests;

/// <summary>
/// Boots the real API against a real PostgreSQL database, with the Python analysis
/// service stubbed at the HTTP handler level.
///
/// Stubbing the handler rather than the client is deliberate: the real
/// <c>HttpAnalysisServiceClient</c> multipart call, response parsing and error mapping
/// all run. Only pandas is replaced, which keeps these tests hermetic while still
/// exercising every real database write. The pandas pipeline itself is covered by the
/// Python suite.
/// </summary>
public sealed class DataraApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"datara_test_{Guid.NewGuid():N}";

    private readonly string _storageRoot =
        Path.Combine(Path.GetTempPath(), "datara-tests", Guid.NewGuid().ToString("N"));

    private string AdminConnectionString =>
        Environment.GetEnvironmentVariable("DATARA_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=postgres;Username=datara;Password=datara";

    private string TestConnectionString => new NpgsqlConnectionStringBuilder(AdminConnectionString)
    {
        Database = _databaseName,
        Pooling = false,
    }.ConnectionString;

    /// <summary>Profile the stubbed analysis service returns for every upload.</summary>
    public DatasetProfileResponse StubProfile { get; } = StubProfileFactory.Build();

    /// <summary>
    /// The database is created in the constructor, before xUnit resolves the host, so
    /// the migration-on-startup path finds a database that already exists.
    /// </summary>
    public DataraApiFactory()
    {
        CreateDatabase();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.UseSetting("ConnectionStrings:Default", TestConnectionString);
        builder.UseSetting("FileStorage:RootPath", _storageRoot);
        builder.UseSetting("Ai:Enabled", "false");

        builder.ConfigureServices(services =>
        {
            // Add the handler to the existing registration rather than calling
            // AddHttpClient again: re-registering would drop the BaseAddress that
            // AddDataraInfrastructure configured, and relative URIs would throw.
            services.Configure<HttpClientFactoryOptions>(
                ServiceCollectionExtensions.HttpClientName,
                options => options.HttpMessageHandlerBuilderActions.Add(
                    builder => builder.PrimaryHandler = new StubAnalysisHandler(StubProfile)));

            services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        });
    }

    /// <summary>Creates an empty database for this fixture.</summary>
    private void CreateDatabase()
    {
        using var connection = new NpgsqlConnection(AdminConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{_databaseName}\";";
        command.ExecuteNonQuery();
    }

    private void DropDatabase()
    {
        try
        {
            using var connection = new NpgsqlConnection(AdminConnectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE);";
            command.ExecuteNonQuery();
        }
        catch (NpgsqlException)
        {
            // A database we could not drop is a cleanup detail, not a test failure.
        }
    }

    /// <summary>Resolves a service from the running host, for asserting on internals.</summary>
    public T Resolve<T>()
        where T : notnull => Services.GetRequiredService<T>();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        DropDatabase();

        try
        {
            if (Directory.Exists(_storageRoot))
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file still held by the OS on a build agent is not worth failing over.
        }
    }
}
