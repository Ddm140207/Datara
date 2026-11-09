namespace Datara.Domain.Entities;

/// <summary>A correlation coefficient between two columns.</summary>
public class CorrelationPairRecord
{
    public string ColumnA { get; set; } = string.Empty;

    public string ColumnB { get; set; } = string.Empty;

    public decimal Coefficient { get; set; }

    public string Method { get; set; } = "pearson";
}