namespace agot_bg_website.Infrastructure.Paging;

public sealed class PagedResult<T>
{
    public required List<T> Items { get; init; }

    public required PagerInfo Pager { get; init; }
}
