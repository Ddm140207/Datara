using Microsoft.EntityFrameworkCore;

namespace Datara.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for Datara. Mappings live in
/// <c>Persistence/Configurations</c> so this file stays a plain aggregate root
/// registry rather than a wall of schema.
/// </summary>
public class DataraDbContext : DbContext
{
    public DataraDbContext(DbContextOptions<DataraDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Dataset> Datasets => Set<Dataset>();

    public DbSet<DatasetColumn> DatasetColumns => Set<DatasetColumn>();

    public DbSet<DatasetAnalysis> DatasetAnalyses => Set<DatasetAnalysis>();

    public DbSet<DataQualityIssue> DataQualityIssues => Set<DataQualityIssue>();

    public DbSet<AnalysisRun> AnalysisRuns => Set<AnalysisRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DataraDbContext).Assembly);
    }

    /// <summary>
    /// Forces every <see cref="DateTimeOffset"/> through UTC. Npgsql refuses to write
    /// a non-zero offset to <c>timestamptz</c>, so a value carrying the server's local
    /// offset would fail at save time. Normalising centrally means no entity has to
    /// remember to do it.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder
            .Properties<DateTimeOffset>()
            .HaveConversion<UtcDateTimeOffsetConverter>();

        configurationBuilder
            .Properties<DateTimeOffset?>()
            .HaveConversion<NullableUtcDateTimeOffsetConverter>();
    }
}
