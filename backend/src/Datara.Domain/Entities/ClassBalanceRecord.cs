namespace Datara.Domain.Entities;

/// <summary>Class distribution of a candidate target column.</summary>
public class ClassBalanceRecord
{
    public string Column { get; set; } = string.Empty;

    public int DistinctClasses { get; set; }

    public decimal MinorityPercentage { get; set; }

    public decimal MajorityPercentage { get; set; }

    public decimal? ImbalanceRatio { get; set; }

    public bool IsImbalanced { get; set; }
}