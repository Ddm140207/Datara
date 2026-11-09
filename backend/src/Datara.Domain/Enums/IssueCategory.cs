namespace Datara.Domain.Enums;

/// <summary>Logical grouping used to filter and aggregate quality issues.</summary>
public enum IssueCategory
{
    MissingValues = 0,
    Duplicates = 1,
    DuplicateColumn = 2,
    TypeValidity = 3,
    Consistency = 4,
    Outliers = 5,
    Cardinality = 6,
    ConstantColumn = 7,
    Leakage = 8,
    ClassImbalance = 9,
    HighDimensionality = 10,
    SmallSample = 11,
    Distribution = 12,
    Identifiers = 13,
}