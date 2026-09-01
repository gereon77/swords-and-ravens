using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Paging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Areas.Admin.Pages.Messages;

public class IndexModel(ApplicationDbContext db) : PageModel
{
    private const int PageSize = 100;

    /// <summary>
    /// Messages must be browsed one room at a time: the table is expected to reach 2M+ rows after
    /// the historical Django data import (see MIGRATION_PLAN.md §11), so a global "all messages"
    /// feed would force an expensive unfiltered scan/count. Filtering by the indexed RoomId first
    /// keeps this cheap regardless of overall table size.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public Guid? RoomId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Page { get; set; } = 1;

    public List<Message> Messages { get; set; } = [];

    public PagerInfo Pager { get; set; } = null!;

    public Room? SelectedRoom { get; set; }

    public List<Room> RecentRooms { get; set; } = [];

    public async Task OnGetAsync()
    {
        RecentRooms = await db.Rooms.OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync();

        if (RoomId is null)
        {
            Pager = new PagerInfo(1, PageSize, 0);
            return;
        }

        SelectedRoom = await db.Rooms.FirstOrDefaultAsync(r => r.Id == RoomId);

        var query = db.Messages.Include(m => m.User).Where(m => m.RoomId == RoomId).AsQueryable();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var normalized = Search.Trim();
            query = query.Where(m => EF.Functions.ILike(m.Text, $"%{normalized}%"));
        }

        var paged = await query.OrderByDescending(m => m.CreatedAt).ToPagedResultAsync(Page, PageSize);
        Messages = paged.Items;
        Pager = paged.Pager;
    }
}
