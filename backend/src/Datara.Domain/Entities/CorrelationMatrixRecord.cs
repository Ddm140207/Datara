namespace Datara.Domain.Entities;

/// <summary>Persisted correlation matrix plus its strongest pairs.</summary>
public class CorrelationMatrixRecord
{
    /// <summary>Correlation method used, for example "pearson".</summary>
    public string Method { get; set; } = "pearson";

    /// <summary>Column ordering shared by every row in <see cref="Values"/>.</summary>
    public List<string> Columns { get; set; } = new();

    /// <summary>Row-major matrix of coefficients. Null entries mean "not computable".</summary>
    public List<List<decimal?>> Values { get; set; } = new();

    /// <summary>Pairs sorted by absolute magnitude, useful for a top-relations list.</summary>
    public List<CorrelationPairRecord> Pairs { get; set; } = new();

    /// <summary>Set when the column set was truncated to bound the matrix size.</summary>
    public string? Note { get; set; }
}