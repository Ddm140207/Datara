using Datara.Application.Analysis;
using Datara.Application.Datasets.Dtos;

namespace Datara.Api.Infrastructure;

/// <summary>
/// Rebuilds a <see cref="DatasetProfileResponse"/> from the persisted DTOs.
///
/// Datara AI needs the same shape the analysis service returns, but re-running
/// pandas for every chat turn would be wasteful. The persisted projection already
/// holds every figure the AI is allowed to see, so it is projected back into the
/// response contract. Fields the API never stores stay null rather than being
/// invented, which keeps the "no invented numbers" guarantee intact.
/// </summary>
internal static class ProfileRehydrator
{
    public static DatasetProfileResponse Rehydrate(DatasetDetailDto dataset, DatasetOverviewDto overview)
    {
        var samples = overview.Columns.Count == 0 ? null : default(SampleRowsResponse);

        return new DatasetProfileResponse
        {
            RowCount = overview.Statistics.RowCount,
            ColumnCount = overview.Statistics.ColumnCount,
            TotalCells = overview.Statistics.TotalCells,
            TotalMissing = overview.Statistics.TotalMissing,
            OverallMissingPercentage = overview.Statistics.OverallMissingPercentage,
            DuplicateRowCount = overview.Statistics.DuplicateRowCount,
            DuplicatePercentage = overview.Statistics.DuplicatePercentage,
            FullyEmptyColumns = overview.Statistics.FullyEmptyColumns,
            MemoryUsageBytes = overview.Statistics.MemoryUsageBytes,
            IngestedAtUtc = overview.AnalyzedAt?.ToString("O") ?? dataset.UploadedAt.ToString("O"),
            Warnings = overview.Warnings,

            Columns = overview.Columns.Select(RehydrateColumn).ToList(),
            QualityIssues = overview.Issues.Select(RehydrateIssue).ToList(),
            Anomalies = overview.Anomalies.Select(a => new AnomalyResponse
            {
                Column = a.Column,
                Kind = a.Kind,
                Description = a.Description,
                Severity = a.Severity,
            }).ToList(),
            Correlations = overview.Correlations is null ? null : new CorrelationMatrixResponse
            {
                Method = overview.Correlations.Method,
                Columns = overview.Correlations.Columns,
                Values = overview.Correlations.Values
                    .Select(row => row.Select(v => v is null ? null : (double?)v.Value).ToList())
                    .ToList(),
                Pairs = overview.Correlations.Pairs
                    .Select(p => new CorrelationPairResponse
                    {
                        ColumnA = p.ColumnA,
                        ColumnB = p.ColumnB,
                        Coefficient = (double)p.Coefficient,
                        Method = p.Method,
                    })
                    .ToList(),
                Note = overview.Correlations.Note,
            },
            MlReadiness = new MlReadinessResponse
            {
                Overall = overview.Readiness.Score,
                Grade = overview.Readiness.Grade,
                Verdict = overview.Readiness.Verdict,
                DataQualityScore = overview.Readiness.DataQualityScore,
                Components = overview.Readiness.Components
                    .Select(c => new ScoreComponentResponse
                    {
                        Key = c.Key,
                        Label = c.Label,
                        Score = c.Score,
                        Weight = c.Weight,
                        Rationale = c.Rationale,
                        Penalties = c.Penalties
                            .Select(p => new ScorePenaltyResponse
                            {
                                Points = p.Points,
                                Reason = p.Reason,
                                Detail = p.Detail,
                            })
                            .ToList(),
                    })
                    .ToList(),
                Strengths = overview.Readiness.Strengths,
                Risks = overview.Readiness.Risks,
                Gates = overview.Readiness.Gates,
                ClassBalance = overview.ClassBalance is null ? null : new ClassBalanceResponse
                {
                    Column = overview.ClassBalance.Column,
                    DistinctClasses = overview.ClassBalance.DistinctClasses,
                    MinorityPercentage = overview.ClassBalance.MinorityPercentage,
                    MajorityPercentage = overview.ClassBalance.MajorityPercentage,
                    ImbalanceRatio = overview.ClassBalance.ImbalanceRatio,
                    IsImbalanced = overview.ClassBalance.IsImbalanced,
                },
                LeakageSignals = overview.LeakageSignals
                    .Select(s => new LeakageSignalResponse
                    {
                        Column = s.Column,
                        Signal = s.Signal,
                        Severity = s.Severity,
                        Rationale = s.Rationale,
                    })
                    .ToList(),
            },
            TargetCandidates = overview.TargetCandidates,
            Samples = samples,
        };
    }

    private static ColumnProfileResponse RehydrateColumn(ColumnDto column) => new()
    {
        Name = column.Name,
        Ordinal = column.Ordinal,
        InferredKind = column.InferredKind,
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
        Numeric = column.Numeric is null ? null : new NumericSummaryResponse
        {
            Mean = D(column.Numeric.Mean),
            Median = D(column.Numeric.Median),
            Std = D(column.Numeric.StdDev),
            Min = D(column.Numeric.Min),
            Max = D(column.Numeric.Max),
            Q1 = D(column.Numeric.Q1),
            Q3 = D(column.Numeric.Q3),
            P05 = D(column.Numeric.P05),
            P25 = D(column.Numeric.P25),
            P75 = D(column.Numeric.P75),
            P95 = D(column.Numeric.P95),
            Skewness = D(column.Numeric.Skewness),
            Kurtosis = D(column.Numeric.Kurtosis),
            Sum = D(column.Numeric.Sum),
            Variance = D(column.Numeric.Variance),
            Range = D(column.Numeric.Range),
            CoefficientOfVariation = D(column.Numeric.CoefficientOfVariation),
            ZerosCount = column.Numeric.ZerosCount,
            NegativeCount = column.Numeric.NegativeCount,
        },
        TopCategories = column.TopCategories
            .Select(c => new CategoryFrequencyResponse
            {
                Value = c.Value,
                Count = c.Count,
                Percentage = c.Percentage,
            })
            .ToList(),
        CategoryPercentageCovered = column.CategoryPercentageCovered,
        Histogram = column.Histogram is null ? null : new HistogramResponse
        {
            BinEdges = column.Histogram.BinEdges,
            Counts = column.Histogram.Counts,
        },
        Outliers = column.Outliers
            .Select(o => new OutlierSummaryResponse
            {
                Method = o.Method,
                LowerBound = D(o.LowerBound),
                UpperBound = D(o.UpperBound),
                Count = o.Count,
                Percentage = o.Percentage,
                Confidence = o.Confidence,
            })
            .ToList(),
        DateMin = column.DateMin?.ToString("O"),
        DateMax = column.DateMax?.ToString("O"),
        SampleValues = column.SampleValues,
        Notes = column.Notes,
    };

    private static QualityIssueResponse RehydrateIssue(QualityIssueDto issue) => new()
    {
        Code = issue.Code,
        Severity = issue.Severity,
        Category = issue.Category,
        Column = issue.Column,
        Title = issue.Title,
        Description = issue.Description,
        Evidence = issue.Evidence,
        Recommendation = issue.Recommendation,
        ScoreImpact = issue.ScoreImpact,
    };

    private static double? D(decimal? value) => value is null ? null : (double?)value.Value;
}
