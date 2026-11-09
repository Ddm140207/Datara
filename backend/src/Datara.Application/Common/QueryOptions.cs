namespace Datara.Application.Common;

/// <summary>Sorting options shared by list endpoints.</summary>
public sealed record SortOptions(string? SortBy, bool Descending)
{
    public static SortOptions None { get; } = new(null, false);
}

/// <summary>Common paging inputs for list endpoints.</summary>
public sealed record PageOptions(int Page, int PageSize)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 200;

    public int NormalisedPage => Page < 1 ? 1 : Page;

    public int NormalisedPageSize => PageSize switch
    {
        < 1 => DefaultPageSize,
        > MaxPageSize => MaxPageSize,
        _ => PageSize,
    };
}