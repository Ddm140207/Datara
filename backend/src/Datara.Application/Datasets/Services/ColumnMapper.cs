using Datara.Application.Datasets.Dtos;
using Datara.Domain.Entities;

namespace Datara.Application.Datasets.Services;

/// <summary>Projects column entities into API DTOs. Entities never leave the service layer.</summary>
internal static class ColumnMapper
{
    public static ColumnDto ToDto(DatasetColumn column) => new()
    {
        Id = column.Id,
        Name = column.Name,
        Ordinal = column.Ordinal,
        InferredKind = column.InferredKind.ToString(),
        PandasDtype = column.PandasDtype,
        NonNullCount = column.NonNullCount,
        NullCount = column.NullCount,
        MissingPercentage = column.MissingPercentage,
        UniqueCount = column.UniqueCount,
        CardinalityRatio = column.CardinalityRatio,
        IsConstant = column.IsConstant,
        MemorySizeBytes = column.MemorySizeBytes,
        LeadingWhitespaceCount = column.LeadingWhitespaceCount,
        MixedTypeCount = column.MixedTypeCount,

        // A column only reports numeric statistics when it actually has them, so the
        // UI can distinguish "no data" from "measured zero".
        Numeric = column.Mean is null && column.Median is null && column.Min is null
            ? null
            : new NumericSummaryDto
            {
                Mean = column.Mean,
                Median = column.Median,
                StdDev = column.StdDev,
                Min = column.Min,
                Max = column.Max,
                Q1 = column.Q1,
                Q3 = column.Q3,
                P05 = column.P05,
                P25 = column.P25,
                P75 = column.P75,
                P95 = column.P95,
                Skewness = column.Skewness,
                Kurtosis = column.Kurtosis,
                Sum = column.Mean is null ? null : null,
                Variance = column.StdDev is null ? null : column.StdDev * column.StdDev,
                Range = column.Min is null || column.Max is null ? null : column.Max - column.Min,
                CoefficientOfVariation =
                    column.Mean is null || column.StdDev is null || column.Mean == 0m
                        ? null
                        : column.StdDev / column.Mean,
                ZerosCount = column.ZerosCount ?? 0,
                NegativeCount = column.NegativeCount ?? 0,
            },

        TopCategories = column.TopCategories
            .Select(c => new CategoryFrequencyDto
            {
                Value = c.Value,
                Count = c.Count,
                Percentage = c.Percentage,
            })
            .ToList(),
        CategoryPercentageCovered = column.CategoryPercentageCovered,

        Histogram = column.HistogramCounts.Count == 0 || column.HistogramBinEdges.Count == 0
            ? null
            : new HistogramDto
            {
                BinEdges = column.HistogramBinEdges,
                Counts = column.HistogramCounts,
            },

        Outliers = column.Outliers
            .Select(o => new OutlierDto
            {
                Method = o.Method,
                LowerBound = o.LowerBound,
                UpperBound = o.UpperBound,
                Count = o.Count,
                Percentage = o.Percentage,
                Confidence = o.Confidence,
            })
            .ToList(),

        DateMin = column.DateMin,
        DateMax = column.DateMax,
        SampleValues = column.SampleValues,
        Notes = column.Notes,
    };
}