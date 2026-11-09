using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Datara.Infrastructure.Persistence.Configurations;

public class DatasetColumnConfiguration : IEntityTypeConfiguration<DatasetColumn>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void Configure(EntityTypeBuilder<DatasetColumn> builder)
    {
        builder.ToTable("dataset_columns");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.DatasetId).HasColumnName("dataset_id");
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(300).IsRequired();
        builder.Property(c => c.Ordinal).HasColumnName("ordinal");
        builder.Property(c => c.InferredKind).HasColumnName("inferred_kind").HasConversion<string>().HasMaxLength(24);
        builder.Property(c => c.PandasDtype).HasColumnName("pandas_dtype").HasMaxLength(64);
        builder.Property(c => c.NonNullCount).HasColumnName("non_null_count");
        builder.Property(c => c.NullCount).HasColumnName("null_count");
        builder.Property(c => c.MissingPercentage).HasColumnName("missing_percentage").HasPrecision(6, 2);
        builder.Property(c => c.UniqueCount).HasColumnName("unique_count");
        builder.Property(c => c.CardinalityRatio).HasColumnName("cardinality_ratio").HasPrecision(8, 4);
        builder.Property(c => c.IsConstant).HasColumnName("is_constant");
        builder.Property(c => c.MemorySizeBytes).HasColumnName("memory_size_bytes");
        builder.Property(c => c.LeadingWhitespaceCount).HasColumnName("leading_whitespace_count");
        builder.Property(c => c.MixedTypeCount).HasColumnName("mixed_type_count");

        builder.Property(c => c.Mean).HasColumnName("mean").HasPrecision(18, 6);
        builder.Property(c => c.Median).HasColumnName("median").HasPrecision(18, 6);
        builder.Property(c => c.StdDev).HasColumnName("std_dev").HasPrecision(18, 6);
        builder.Property(c => c.Min).HasColumnName("min").HasPrecision(18, 6);
        builder.Property(c => c.Max).HasColumnName("max").HasPrecision(18, 6);
        builder.Property(c => c.Q1).HasColumnName("q1").HasPrecision(18, 6);
        builder.Property(c => c.Q3).HasColumnName("q3").HasPrecision(18, 6);
        builder.Property(c => c.P05).HasColumnName("p05").HasPrecision(18, 6);
        builder.Property(c => c.P25).HasColumnName("p25").HasPrecision(18, 6);
        builder.Property(c => c.P75).HasColumnName("p75").HasPrecision(18, 6);
        builder.Property(c => c.P95).HasColumnName("p95").HasPrecision(18, 6);
        builder.Property(c => c.Skewness).HasColumnName("skewness").HasPrecision(18, 6);
        builder.Property(c => c.Kurtosis).HasColumnName("kurtosis").HasPrecision(18, 6);
        builder.Property(c => c.ZerosCount).HasColumnName("zeros_count");
        builder.Property(c => c.NegativeCount).HasColumnName("negative_count");

        builder.Property(c => c.CategoryPercentageCovered).HasColumnName("category_percentage_covered").HasPrecision(6, 2);

        // Histogram bin edges are floats in the wire contract; postgres stores them
        // as double precision which round-trips exactly for our purposes.
        builder.Property(c => c.HistogramBinEdges).HasColumnName("histogram_bin_edges").HasColumnType("double precision[]");
        builder.Property(c => c.HistogramCounts).HasColumnName("histogram_counts").HasColumnType("integer[]");
        builder.Property(c => c.SampleValues).HasColumnName("sample_values").HasColumnType("text[]");
        builder.Property(c => c.Notes).HasColumnName("notes").HasColumnType("text[]");

        // Category counts and outlier windows are variable-length nested objects that
        // are always read and written as a unit, so jsonb beats a child table here.
        builder.Property(c => c.TopCategories)
            .HasColumnName("top_categories")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? "[]" : JsonSerializer.Serialize(v, JsonOptions),
                v => string.IsNullOrWhiteSpace(v) ? new List<CategoryFrequency>() : (JsonSerializer.Deserialize<List<CategoryFrequency>>(v, JsonOptions) ?? new List<CategoryFrequency>()))
            .Metadata.SetValueComparer(JsonValueComparer.ForElements<CategoryFrequency>());

        builder.Property(c => c.Outliers)
            .HasColumnName("outliers")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? "[]" : JsonSerializer.Serialize(v, JsonOptions),
                v => string.IsNullOrWhiteSpace(v) ? new List<OutlierRecord>() : (JsonSerializer.Deserialize<List<OutlierRecord>>(v, JsonOptions) ?? new List<OutlierRecord>()))
            .Metadata.SetValueComparer(JsonValueComparer.ForElements<OutlierRecord>());

        builder.Property(c => c.DateMin).HasColumnName("date_min");
        builder.Property(c => c.DateMax).HasColumnName("date_max");

        builder.HasIndex(c => new { c.DatasetId, c.Name })
            .IsUnique()
            .HasDatabaseName("ix_dataset_columns_dataset_name");

        builder.HasOne(c => c.Dataset)
            .WithMany(d => d.Columns)
            .HasForeignKey(c => c.DatasetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
