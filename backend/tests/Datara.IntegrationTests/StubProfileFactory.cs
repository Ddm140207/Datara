using Datara.Application.Analysis;

namespace Datara.IntegrationTests;

/// <summary>
/// A small but structurally complete profile. It intentionally includes the awkward
/// cases - null numeric stats, an empty category list, a jsonb correlation matrix and
/// sample rows - so persistence round-tripping is exercised rather than just the
/// happy path.
/// </summary>
internal static class StubProfileFactory
{
    public static DatasetProfileResponse Build() => new()
    {
        RowCount = 4,
        ColumnCount = 5,
        TotalCells = 20,
        TotalMissing = 3,
        OverallMissingPercentage = 15.00m,
        DuplicateRowCount = 1,
        DuplicatePercentage = 25.00m,
        FullyEmptyColumns = new List<string> { "notes" },
        MemoryUsageBytes = 4096,
        IngestedAtUtc = "2026-01-01T00:00:00Z",
        Warnings = new List<string> { "1 row had a token count mismatch" },
        ParseErrors = new List<string> { "line 5: expected 5 fields, found 4" },

        Columns = new List<ColumnProfileResponse>
        {
            new()
            {
                Name = "customer_id",
                Ordinal = 0,
                InferredKind = "identifier",
                PandasDtype = "object",
                NonNullCount = 4,
                NullCount = 0,
                MissingPercentage = 0m,
                UniqueCount = 4,
                CardinalityRatio = 1.0m,
                IsConstant = false,
                SampleValues = new List<string> { "C-1", "C-2", "C-3", "C-4" },
                Notes = new List<string> { "high cardinality; identifier-like" },
            },
            new()
            {
                Name = "monthly_spend",
                Ordinal = 1,
                InferredKind = "numeric",
                PandasDtype = "float64",
                NonNullCount = 3,
                NullCount = 1,
                MissingPercentage = 25.00m,
                UniqueCount = 3,
                CardinalityRatio = 0.75m,
                IsConstant = false,
                MemorySizeBytes = 256,
                Numeric = new NumericSummaryResponse
                {
                    Mean = 42.5,
                    Median = 40.0,
                    Std = 12.25,
                    Min = 22.5,
                    Max = 65.0,
                    Q1 = 35.0,
                    Q3 = 52.0,
                    P05 = 25.0,
                    P25 = 35.0,
                    P75 = 52.0,
                    P95 = 62.0,
                    Skewness = 0.42,
                    Kurtosis = -0.15,
                    Sum = 127.5,
                    Variance = 150.0625,
                    Range = 42.5,
                    CoefficientOfVariation = 0.2882,
                    ZerosCount = 0,
                    NegativeCount = 0,
                },
                Histogram = new HistogramResponse
                {
                    BinEdges = new List<double> { 20.0, 40.0, 60.0 },
                    Counts = new List<int> { 1, 2 },
                },
                Outliers = new List<OutlierSummaryResponse>
                {
                    new()
                    {
                        Method = "iqr",
                        LowerBound = 9.5,
                        UpperBound = 77.5,
                        Count = 0,
                        Percentage = 0m,
                        Confidence = "0.95",
                    },
                },
                SampleValues = new List<string> { "22.5", "40.0", "65.0" },
            },
            new()
            {
                // Exercises the null-numeric branch: a categorical column has no stats.
                Name = "plan",
                Ordinal = 2,
                InferredKind = "categorical",
                PandasDtype = "object",
                NonNullCount = 3,
                NullCount = 1,
                MissingPercentage = 25.00m,
                UniqueCount = 2,
                CardinalityRatio = 0.5m,
                IsConstant = false,
                TopCategories = new List<CategoryFrequencyResponse>
                {
                    new() { Value = "pro", Count = 2, Percentage = 66.67m },
                    new() { Value = "free", Count = 1, Percentage = 33.33m },
                },
                CategoryPercentageCovered = 100.00m,
                SampleValues = new List<string> { "pro", "free" },
            },
            new()
            {
                // Constant column: the edge case readiness scoring penalises.
                Name = "region",
                Ordinal = 3,
                InferredKind = "categorical",
                PandasDtype = "object",
                NonNullCount = 4,
                NullCount = 0,
                MissingPercentage = 0m,
                UniqueCount = 1,
                CardinalityRatio = 0.25m,
                IsConstant = true,
                TopCategories = new List<CategoryFrequencyResponse>
                {
                    new() { Value = "EU", Count = 4, Percentage = 100m },
                },
                CategoryPercentageCovered = 100m,
                SampleValues = new List<string> { "EU" },
                Notes = new List<string> { "single constant value" },
            },
            new()
            {
                Name = "signed_up_at",
                Ordinal = 4,
                InferredKind = "datetime",
                PandasDtype = "object",
                NonNullCount = 4,
                NullCount = 0,
                MissingPercentage = 0m,
                UniqueCount = 4,
                CardinalityRatio = 1.0m,
                IsConstant = false,
                DateMin = "2024-01-01T00:00:00",
                DateMax = "2024-06-01T00:00:00",
                SampleValues = new List<string> { "2024-01-01", "2024-06-01" },
            },
        },

        Correlations = new CorrelationMatrixResponse
        {
            Method = "pearson",
            Columns = new List<string> { "monthly_spend", "tenure_months", "constant_missing" },
            Values = new List<List<double?>>
            {
                new() { 1.0, -0.42, null },
                new() { -0.42, 1.0, null },
                new() { null, null, 1.0 },
            },
            Pairs = new List<CorrelationPairResponse>
            {
                new() { ColumnA = "monthly_spend", ColumnB = "tenure_months", Coefficient = -0.42, Method = "pearson" },
            },
            Note = "null where the pair is not computable",
        },

        Anomalies = new List<AnomalyResponse>
        {
            new()
            {
                Column = "monthly_spend",
                Kind = "distribution_shift",
                Description = "Right-skewed distribution detected.",
                Severity = "warning",
                Evidence = new Dictionary<string, object?> { ["skewness"] = 0.42 },
            },
        },

        QualityIssues = new List<QualityIssueResponse>
        {
            new()
            {
                Code = "high_missingness",
                Severity = "high",
                Category = "completeness",
                Column = "monthly_spend",
                Title = "High missingness in monthly_spend",
                Description = "25.00% of values are missing.",
                Recommendation = "Impute or drop before training.",
                ScoreImpact = -12.5m,
                Evidence = new Dictionary<string, object?> { ["missing_percentage"] = 25.00 },
            },
            new()
            {
                Code = "constant_column",
                Severity = "medium",
                Category = "usability",
                Column = "region",
                Title = "Constant column: region",
                Description = "Only one distinct value is present.",
                Recommendation = "Remove; it carries no signal.",
                ScoreImpact = -5.0m,
                Evidence = new Dictionary<string, object?> { ["unique_count"] = 1 },
            },
        },

        MlReadiness = new MlReadinessResponse
        {
            Overall = 58.25m,
            Grade = "C",
            Verdict = "Usable after cleanup.",
            DataQualityScore = 61.00m,
            Components = new List<ScoreComponentResponse>
            {
                new()
                {
                    Key = "completeness",
                    Label = "Completeness",
                    Score = 75.00m,
                    Weight = 0.22m,
                    Rationale = "15% of cells missing overall.",
                    Penalties = new List<ScorePenaltyResponse>
                    {
                        new() { Points = -25.00m, Reason = "high_missingness", Detail = "monthly_spend" },
                    },
                },
                new()
                {
                    Key = "uniqueness",
                    Label = "Uniqueness",
                    Score = 90.00m,
                    Weight = 0.12m,
                    Rationale = "One duplicate row.",
                },
            },
            ClassBalance = new ClassBalanceResponse
            {
                Column = "churned",
                DistinctClasses = 2,
                MinorityPercentage = 25.00m,
                MajorityPercentage = 75.00m,
                ImbalanceRatio = 3.0m,
                IsImbalanced = true,
            },
            LeakageSignals = new List<LeakageSignalResponse>
            {
                new()
                {
                    Column = "cancellation_reason",
                    Signal = "post_outcome_field",
                    Severity = "low",
                    Rationale = "Recorded after churn is known.",
                },
            },
            Strengths = new List<string> { "Low duplicate rate" },
            Risks = new List<string> { "High missingness in monthly_spend" },
            Gates = new List<string> { "Resolve high-severity quality issues" },
        },

        TargetCandidates = new List<string> { "churned" },

        Samples = new SampleRowsResponse
        {
            Columns = new List<string> { "customer_id", "monthly_spend", "plan", "region", "signed_up_at" },
            Rows = new List<List<string>>
            {
                new() { "C-1", "22.5", "pro", "EU", "2024-01-01" },
                new() { "C-2", "40.0", "free", "EU", "2024-02-01" },
            },
            Truncated = false,
        },
    };
}
