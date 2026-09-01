using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Paging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Areas.Admin.Pages.Rooms;

public class IndexModel(ApplicationDbContext db) : PageModel
{
    private const int PageSize = 50;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Page { get; set; } = 1;

    public List<Room> Rooms { get; set; } = [];

    public Dictionary<Guid, int> MessageCountByRoomId { get; set; } = [];

    public PagerInfo Pager { get; set; } = null!;

    public async Task OnGetAsync()
    {
        var query = db.Rooms.AsQueryable();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var normalized = Search.Trim();
            query = query.Where(r =>
                EF.Functions.ILike(r.Name, $"%{normalized}%") ||
                r.Id.ToString() == normalized);
        }

        var paged = await query.OrderByDescending(r => r.CreatedAt).ToPagedResultAsync(Page, PageSize);
        Rooms = paged.Items;
        Pager = paged.Pager;

        var roomIds = Rooms.Select(r => r.Id).ToList();
        MessageCountByRoomId = await db.Messages
            .Where(m => roomIds.Contains(m.RoomId))
            .GroupBy(m => m.RoomId)
            .Select(g => new { RoomId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoomId, x => x.Count);
    }
}
