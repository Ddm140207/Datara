namespace Datara.Domain.Entities;

/// <summary>Outliers detected for a numeric column by one specific method.</summary>
public class OutlierRecord
{
    /// <summary>Detection method, for example "IQR" or "Z-Score".</summary>
    public string Method { get; set; } = string.Empty;

    public decimal? LowerBound { get; set; }

    public decimal? UpperBound { get; set; }

    public int Count { get; set; }

    public decimal Percentage { get; set; }

    /// <summary>High, Medium or Low: how much to trust this detection.</summary>
    public string Confidence { get; set; } = "Medium";
}