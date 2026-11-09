namespace Datara.Domain.Entities;

/// <summary>A single value and its frequency within a categorical column.</summary>
public class CategoryFrequency
{
    public string Value { get; set; } = string.Empty;

    public int Count { get; set; }

    public decimal Percentage { get; set; }
}