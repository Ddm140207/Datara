using Datara.Application.Analysis;
using Datara.Application.Ai;
using Datara.Application.Ai.Abstractions;
using Datara.Application.Common;
using Datara.Application.Datasets.Abstractions;
using Datara.Application.Datasets.Services;
using Datara.Domain.Entities;
using Datara.Infrastructure.Ai;
using Datara.Infrastructure.Analysis;
using Datara.Infrastructure.Persistence;
using Datara.Infrastructure.Persistence.Repositories;
using Datara.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Datara.Infrastructure;

/// <summary>Wires Infrastructure into the DI container.</summary>
public static class ServiceCollectionExtensions
{
    public const string HttpClientName = "datara-analysis";
    public const string LlmHttpClientName = "datara-llm";

    public static IServiceCollection AddDataraInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
                               ?? "Host=localhost;Port=5432;Database=datara;Username=datara;Password=datara";

        services.AddDbContext<DataraDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(DataraDbContext).Assembly.FullName);
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, TimeSpan.FromSeconds(5), null);
            }));

        services.Configure<AnalysisServiceOptions>(
            configuration.GetSection(AnalysisServiceOptions.SectionName));

        services.Configure<FileStorageOptions>(
            configuration.GetSection(FileStorageOptions.SectionName));

        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));

        var analysisOptions = configuration
            .GetSection(AnalysisServiceOptions.SectionName)
            .Get<AnalysisServiceOptions>() ?? new AnalysisServiceOptions();

        services.AddHttpClient(HttpClientName, client =>
        {
            client.BaseAddress = new Uri(analysisOptions.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        services.AddHttpClient(LlmHttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddScoped<IDatasetRepository, DatasetRepository>();
        services.AddScoped<IAnalysisServiceClient, HttpAnalysisServiceClient>();

        services.AddApplicationServices();

        return services;
    }

    /// <summary>Registers the application layer. Split out so tests can compose it alone.</summary>
    private static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IDatasetService, DatasetService>();
        services.AddSingleton<IDataraAiService, DataraAiService>();
        services.AddSingleton<IDatasetContextBuilder, DatasetContextBuilder>();
        services.AddSingleton<IDeterministicAiFallback, DeterministicAiFallback>();

        // The provider is resolved per call so switching Ai:Provider in configuration
        // needs no rebuild, and so a disabled feature never touches the network.
        services.AddSingleton<ILlmProvider>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            var http = sp.GetRequiredService<IHttpClientFactory>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            return options.Provider.ToLowerInvariant() switch
            {
                "anthropic" => new AnthropicChatProvider(
                    http.CreateClient(LlmHttpClientName),
                    options,
                    loggerFactory.CreateLogger<AnthropicChatProvider>()),
                "ollama" => new OllamaChatProvider(
                    http.CreateClient(LlmHttpClientName),
                    options,
                    loggerFactory.CreateLogger<OllamaChatProvider>()),
                _ => new OpenAiChatProvider(
                    http.CreateClient(LlmHttpClientName),
                    options,
                    loggerFactory.CreateLogger<OpenAiChatProvider>()),
            };
        });

        // DataraAiService takes the concrete options, so bind it once as a singleton.
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AiOptions>>().Value);

        services.AddScoped<IClock>(_ => new SystemClock());

        return services;
    }

    /// <summary>
    /// Ensures the seeded owner exists. Datara is single-tenant for the MVP, so the
    /// seed is deterministic rather than a real authentication flow.
    /// </summary>
    public static async Task SeedAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataraDbContext>();

        var ownerId = new Guid("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

        var exists = await db.Users.AnyAsync(u => u.Id == ownerId, cancellationToken);
        if (exists)
        {
            return;
        }

        db.Users.Add(new User
        {
            Id = ownerId,
            Email = "owner@datara.local",
            DisplayName = "Datara Owner",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Brings the schema up to date and seeds reference data. A failure here is
    /// logged rather than fatal so the API can still start and report degraded
    /// health against a database that is still booting.
    /// </summary>
    public static async Task MigrateAndSeedAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Datara.Startup");

        try
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DataraDbContext>();

            // Bounded: compose must not hang forever waiting for postgres.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));

            await db.Database.MigrateAsync(timeout.Token);
            await services.SeedAsync(timeout.Token);

            logger.LogInformation("Database schema is up to date and seed data is present.");
        }
        catch (OperationCanceledException)
        {
            logger.LogError("Database migration timed out. The database is probably unreachable.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database migration failed. The API will start in a degraded state.");
        }
    }
}
