namespace Datara.Application.Datasets.Dtos;

public sealed record ColumnDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int Ordinal { get; init; }

    public string InferredKind { get; init; } = string.Empty;

    public string PandasDtype { get; init; } = string.Empty;

    public int NonNullCount { get; init; }

    public int NullCount { get; init; }

    public decimal MissingPercentage { get; init; }

    public int UniqueCount { get; init; }

    public decimal CardinalityRatio { get; init; }

    public bool IsConstant { get; init; }

    public long? MemorySizeBytes { get; init; }

    public int LeadingWhitespaceCount { get; init; }

    public int MixedTypeCount { get; init; }

    public NumericSummaryDto? Numeric { get; init; }

    public List<CategoryFrequencyDto> TopCategories { get; init; } = new();

    public decimal CategoryPercentageCovered { get; init; }

    public HistogramDto? Histogram { get; init; }

    public List<OutlierDto> Outliers { get; init; } = new();

    public DateTimeOffset? DateMin { get; init; }

    public DateTimeOffset? DateMax { get; init; }

    public List<string> SampleValues { get; init; } = new();

    public List<string> Notes { get; init; } = new();
}

public sealed record NumericSummaryDto
{
    public decimal? Mean { get; init; }

    public decimal? Median { get; init; }

    public decimal? StdDev { get; init; }

    public decimal? Min { get; init; }

    public decimal? Max { get; init; }

    public decimal? Q1 { get; init; }

    public decimal? Q3 { get; init; }

    public decimal? P05 { get; init; }

    public decimal? P25 { get; init; }

    public decimal? P75 { get; init; }

    public decimal? P95 { get; init; }

    public decimal? Skewness { get; init; }

    public decimal? Kurtosis { get; init; }

    public decimal? Sum { get; init; }

    public decimal? Variance { get; init; }

    public decimal? Range { get; init; }

    public decimal? CoefficientOfVariation { get; init; }

    public int ZerosCount { get; init; }

    public int NegativeCount { get; init; }
}

public sealed record CategoryFrequencyDto
{
    public string Value { get; init; } = string.Empty;

    public int Count { get; init; }

    public decimal Percentage { get; init; }
}

public sealed record HistogramDto
{
    public List<double> BinEdges { get; init; } = new();

    public List<int> Counts { get; init; } = new();
}

public sealed record OutlierDto
{
    public string Method { get; init; } = string.Empty;

    public decimal? LowerBound { get; init; }

    public decimal? UpperBound { get; init; }

    public int Count { get; init; }

    public decimal Percentage { get; init; }

    public string Confidence { get; init; } = string.Empty;
}