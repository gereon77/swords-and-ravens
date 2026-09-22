namespace agot_bg_website.Infrastructure.Paging;

/// <summary>
/// Minimal state a "Page N of M" control needs to render, shared across every admin list page
/// (Games/Rooms/Messages/Users) that can no longer render all its rows at once — see
/// PagingExtensions.ToPagedResultAsync and Pages/Shared/_Pager.cshtml.
/// </summary>
public sealed record PagerInfo(int PageNumber, int PageSize, long TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => PageNumber > 1;

    public bool HasNext => PageNumber < TotalPages;
}
