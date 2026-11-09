using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Datara.Infrastructure.Persistence.Configurations;

public class AnalysisRunConfiguration : IEntityTypeConfiguration<AnalysisRun>
{
    public void Configure(EntityTypeBuilder<AnalysisRun> builder)
    {
        builder.ToTable("analysis_runs");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.DatasetId).HasColumnName("dataset_id");
        builder.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Trigger).HasColumnName("trigger").HasConversion<string>().HasMaxLength(24);
        builder.Property(r => r.StartedAt).HasColumnName("started_at");
        builder.Property(r => r.CompletedAt).HasColumnName("completed_at");
        builder.Property(r => r.DurationMs).HasColumnName("duration_ms");
        builder.Property(r => r.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
        builder.Property(r => r.CorrelationId).HasColumnName("correlation_id").HasMaxLength(64);

        builder.HasIndex(r => new { r.DatasetId, r.StartedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_analysis_runs_dataset_started");

        builder.HasOne(r => r.Dataset)
            .WithMany(d => d.Runs)
            .HasForeignKey(r => r.DatasetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
