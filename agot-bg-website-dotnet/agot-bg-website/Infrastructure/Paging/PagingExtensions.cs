using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Infrastructure.Paging;

/// <summary>
/// Admin list pages (Games/Rooms/Messages/Users) used to just Take(100), which stopped scaling
/// once the historical Django data (tens of thousands of games, 2M+ chat messages, see
/// MIGRATION_PLAN.md §11) gets imported. This is plain offset (Skip/Take) pagination rather than
/// keyset/cursor pagination — simpler to wire up, and acceptable here because every admin list is
/// either already reasonably small (Games/Rooms number in the tens of thousands at most) or, for
/// Messages, always filtered down to a single Room first (via an indexed RoomId), so COUNT/OFFSET
/// stay cheap even though the underlying table is huge.
/// </summary>
public static class PagingExtensions
{
    /// <summary>
    /// Smallest/largest page size a user is allowed to pick via the "Items per page" control on
    /// <c>Pages/Shared/_Pager.cshtml</c> - keeps a tampered querystring/localStorage value from
    /// forcing an unreasonably large (or a useless zero/negative) page.
    /// </summary>
    public const int MinPageSize = 5;
    public const int MaxPageSize = 500;

    /// <summary>
    /// Clamps a user-supplied (querystring or localStorage-sourced) page size into
    /// [<see cref="MinPageSize"/>, <see cref="MaxPageSize"/>], falling back to
    /// <paramref name="defaultPageSize"/> when absent or not a positive number.
    /// </summary>
    public static int NormalizePageSize(int requestedPageSize, int defaultPageSize) =>
        Math.Clamp(
            requestedPageSize <= 0 ? defaultPageSize : requestedPageSize,
            MinPageSize,
            MaxPageSize
        );

    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        int pageNumber,
        int pageSize
    )
    {
        pageNumber = Math.Max(1, pageNumber);

        var totalCount = await query.LongCountAsync();
        var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<T>
        {
            Items = items,
            Pager = new PagerInfo(pageNumber, pageSize, totalCount),
        };
    }
}
