using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Datara.Infrastructure.Persistence.Configurations;

public class DatasetConfiguration : IEntityTypeConfiguration<Dataset>
{
    public void Configure(EntityTypeBuilder<Dataset> builder)
    {
        builder.ToTable("datasets");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id).HasColumnName("id");
        builder.Property(d => d.OwnerId).HasColumnName("owner_id");
        builder.Property(d => d.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(d => d.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(255).IsRequired();
        builder.Property(d => d.StorageKey).HasColumnName("storage_key").HasMaxLength(512).IsRequired();
        builder.Property(d => d.FileSizeBytes).HasColumnName("file_size_bytes");
        builder.Property(d => d.ContentType).HasColumnName("content_type").HasMaxLength(100);
        builder.Property(d => d.Format).HasColumnName("format").HasMaxLength(16);
        builder.Property(d => d.RowCount).HasColumnName("row_count");
        builder.Property(d => d.ColumnCount).HasColumnName("column_count");
        builder.Property(d => d.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        builder.Property(d => d.MlReadinessScore).HasColumnName("ml_readiness_score").HasPrecision(6, 2);
        builder.Property(d => d.DataQualityScore).HasColumnName("data_quality_score").HasPrecision(6, 2);
        builder.Property(d => d.UploadedAt).HasColumnName("uploaded_at");
        builder.Property(d => d.AnalyzedAt).HasColumnName("analyzed_at");
        builder.Property(d => d.TargetColumn).HasColumnName("target_column").HasMaxLength(200);
        builder.Property(d => d.Description).HasColumnName("description").HasMaxLength(2000);

        // One composite index serves the list query, which always filters by owner
        // and sorts by recency.
        builder.HasIndex(d => new { d.OwnerId, d.UploadedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_datasets_owner_uploaded");

        builder.HasOne(d => d.Owner)
            .WithMany(u => u.Datasets)
            .HasForeignKey(d => d.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
