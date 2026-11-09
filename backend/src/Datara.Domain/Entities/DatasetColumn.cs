using Datara.Domain.Enums;

namespace Datara.Domain.Entities;

/// <summary>Persisted profile of a single dataset column.</summary>
public class DatasetColumn
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DatasetId { get; set; }

    public Dataset? Dataset { get; set; }

    /// <summary>Column name exactly as normalised during ingestion.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Zero-based position in the source file.</summary>
    public int Ordinal { get; set; }

    public ColumnKind InferredKind { get; set; } = ColumnKind.Unknown;

    /// <summary>Underlying pandas dtype string, kept for debugging and transparency.</summary>
    public string PandasDtype { get; set; } = string.Empty;

    public int NonNullCount { get; set; }

    public int NullCount { get; set; }

    public decimal MissingPercentage { get; set; }

    public int UniqueCount { get; set; }

    public decimal CardinalityRatio { get; set; }

    public bool IsConstant { get; set; }

    public long? MemorySizeBytes { get; set; }

    public int LeadingWhitespaceCount { get; set; }

    public int MixedTypeCount { get; set; }

    // ---- numeric statistics (null for non-numeric columns) ------------------
    public decimal? Mean { get; set; }

    public decimal? Median { get; set; }

    public decimal? StdDev { get; set; }

    public decimal? Min { get; set; }

    public decimal? Max { get; set; }

    public decimal? Q1 { get; set; }

    public decimal? Q3 { get; set; }

    public decimal? P05 { get; set; }

    public decimal? P25 { get; set; }

    public decimal? P75 { get; set; }

    public decimal? P95 { get; set; }

    public decimal? Skewness { get; set; }

    public decimal? Kurtosis { get; set; }

    public int? ZerosCount { get; set; }

    public int? NegativeCount { get; set; }

    // ---- categorical statistics --------------------------------------------
    public List<CategoryFrequency> TopCategories { get; set; } = new();

    public decimal CategoryPercentageCovered { get; set; }

    // ---- histogram ----------------------------------------------------------
    public List<double> HistogramBinEdges { get; set; } = new();

    public List<int> HistogramCounts { get; set; } = new();

    // ---- outliers -----------------------------------------------------------
    public List<OutlierRecord> Outliers { get; set; } = new();

    // ---- datetime -----------------------------------------------------------
    public DateTimeOffset? DateMin { get; set; }

    public DateTimeOffset? DateMax { get; set; }

    public List<string> SampleValues { get; set; } = new();

    public List<string> Notes { get; set; } = new();
}