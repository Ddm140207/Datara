namespace Datara.Domain.Enums;

/// <summary>Semantic type inferred for a column during profiling.</summary>
public enum ColumnKind
{
    Unknown = 0,
    Numeric = 1,
    Integer = 2,
    Boolean = 3,
    Categorical = 4,
    Text = 5,
    DateTime = 6,
    Constant = 7,
}