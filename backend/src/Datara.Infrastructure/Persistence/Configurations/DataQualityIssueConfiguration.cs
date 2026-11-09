using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Datara.Infrastructure.Persistence.Configurations;

public class DataQualityIssueConfiguration : IEntityTypeConfiguration<DataQualityIssue>
{
    public void Configure(EntityTypeBuilder<DataQualityIssue> builder)
    {
        builder.ToTable("data_quality_issues");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id).HasColumnName("id");
        builder.Property(i => i.DatasetId).HasColumnName("dataset_id");
        builder.Property(i => i.DatasetAnalysisId).HasColumnName("dataset_analysis_id");
        builder.Property(i => i.Code).HasColumnName("code").HasMaxLength(200).IsRequired();
        builder.Property(i => i.Severity).HasColumnName("severity").HasConversion<string>().HasMaxLength(16);
        builder.Property(i => i.Category).HasColumnName("category").HasConversion<string>().HasMaxLength(24);
        builder.Property(i => i.Column).HasColumnName("column").HasMaxLength(300);
        builder.Property(i => i.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
        builder.Property(i => i.Description).HasColumnName("description").HasMaxLength(4000).IsRequired();
        builder.Property(i => i.EvidenceJson).HasColumnName("evidence").HasColumnType("jsonb");
        builder.Property(i => i.Recommendation).HasColumnName("recommendation").HasMaxLength(2000);
        builder.Property(i => i.ScoreImpact).HasColumnName("score_impact").HasPrecision(6, 2);
        builder.Property(i => i.DetectedAt).HasColumnName("detected_at");

        // Covers the dashboard's filtered issue list.
        builder.HasIndex(i => new { i.DatasetId, i.Severity, i.Category })
            .HasDatabaseName("ix_issues_dataset_severity_category");

        builder.HasIndex(i => new { i.DatasetId, i.Column })
            .HasDatabaseName("ix_issues_dataset_column");

        builder.HasOne(i => i.Dataset)
            .WithMany(d => d.Issues)
            .HasForeignKey(i => i.DatasetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.DatasetAnalysis)
            .WithMany()
            .HasForeignKey(i => i.DatasetAnalysisId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
