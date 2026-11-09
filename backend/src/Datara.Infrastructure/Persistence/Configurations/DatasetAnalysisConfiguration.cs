using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Datara.Infrastructure.Persistence.Configurations;

public class DatasetAnalysisConfiguration : IEntityTypeConfiguration<DatasetAnalysis>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void Configure(EntityTypeBuilder<DatasetAnalysis> builder)
    {
        builder.ToTable("dataset_analyses");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.DatasetId).HasColumnName("dataset_id");
        builder.Property(a => a.AnalysisRunId).HasColumnName("analysis_run_id");

        builder.Property(a => a.RowCount).HasColumnName("row_count");
        builder.Property(a => a.ColumnCount).HasColumnName("column_count");
        builder.Property(a => a.TotalCells).HasColumnName("total_cells");
        builder.Property(a => a.TotalMissing).HasColumnName("total_missing");
        builder.Property(a => a.OverallMissingPercentage).HasColumnName("overall_missing_percentage").HasPrecision(6, 2);
        builder.Property(a => a.DuplicateRowCount).HasColumnName("duplicate_row_count");
        builder.Property(a => a.DuplicatePercentage).HasColumnName("duplicate_percentage").HasPrecision(6, 2);
        builder.Property(a => a.FullyEmptyColumns).HasColumnName("fully_empty_columns").HasColumnType("text[]");
        builder.Property(a => a.MemoryUsageBytes).HasColumnName("memory_usage_bytes");

        builder.Property(a => a.MlReadinessScore).HasColumnName("ml_readiness_score").HasPrecision(6, 2);
        builder.Property(a => a.DataQualityScore).HasColumnName("data_quality_score").HasPrecision(6, 2);
        builder.Property(a => a.Grade).HasColumnName("grade").HasMaxLength(2);
        builder.Property(a => a.Verdict).HasColumnName("verdict").HasMaxLength(500);

        builder.Property(a => a.Strengths).HasColumnName("strengths").HasColumnType("text[]");
        builder.Property(a => a.Risks).HasColumnName("risks").HasColumnType("text[]");
        builder.Property(a => a.Gates).HasColumnName("gates").HasColumnType("text[]");
        builder.Property(a => a.TargetCandidates).HasColumnName("target_candidates").HasColumnType("text[]");
        builder.Property(a => a.Warnings).HasColumnName("warnings").HasColumnType("text[]");
        builder.Property(a => a.ParseErrors).HasColumnName("parse_errors").HasColumnType("text[]");
        builder.Property(a => a.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);

        builder.Property(a => a.CreatedAt).HasColumnName("created_at");

        // Rich nested structures live as jsonb. They are read as a whole graph and
        // never queried field-by-field, so a document column is the right shape.
        builder.Property(a => a.ScoreComponents)
            .HasColumnName("score_components")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => string.IsNullOrWhiteSpace(v) ? new List<ScoreComponent>() : (JsonSerializer.Deserialize<List<ScoreComponent>>(v, JsonOptions) ?? new List<ScoreComponent>()))
            .Metadata.SetValueComparer(JsonValueComparer.ForElements<ScoreComponent>());

        builder.Property(a => a.LeakageSignals)
            .HasColumnName("leakage_signals")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? "[]" : JsonSerializer.Serialize(v, JsonOptions),
                v => string.IsNullOrWhiteSpace(v) ? new List<LeakageSignal>() : (JsonSerializer.Deserialize<List<LeakageSignal>>(v, JsonOptions) ?? new List<LeakageSignal>()))
            .Metadata.SetValueComparer(JsonValueComparer.ForElements<LeakageSignal>());

        builder.Property(a => a.Anomalies)
            .HasColumnName("anomalies")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? "[]" : JsonSerializer.Serialize(v, JsonOptions),
                v => string.IsNullOrWhiteSpace(v) ? new List<AnomalyRecord>() : (JsonSerializer.Deserialize<List<AnomalyRecord>>(v, JsonOptions) ?? new List<AnomalyRecord>()))
            .Metadata.SetValueComparer(JsonValueComparer.ForElements<AnomalyRecord>());

        builder.Property(a => a.Correlations)
            .HasColumnName("correlations")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
                v => v == null ? null : JsonSerializer.Deserialize<CorrelationMatrixRecord>(v, JsonOptions));

        builder.Property(a => a.ClassBalance)
            .HasColumnName("class_balance")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
                v => v == null ? null : JsonSerializer.Deserialize<ClassBalanceRecord>(v, JsonOptions));

        builder.HasIndex(a => new { a.DatasetId, a.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_dataset_analyses_dataset_created");

        builder.HasOne(a => a.Dataset)
            .WithMany(d => d.Analyses)
            .HasForeignKey(a => a.DatasetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.AnalysisRun)
            .WithMany(r => r.Analyses)
            .HasForeignKey(a => a.AnalysisRunId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
