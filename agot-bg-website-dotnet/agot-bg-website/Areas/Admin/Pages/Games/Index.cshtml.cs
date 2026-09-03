using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Paging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Areas.Admin.Pages.Games;

public class IndexModel(ApplicationDbContext db) : PageModel
{
    private const int DefaultPageSize = 25;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = DefaultPageSize;

    public List<Game> Games { get; set; } = [];

    public PagerInfo Pager { get; set; } = null!;

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        PageSize = PagingExtensions.NormalizePageSize(PageSize, DefaultPageSize);

        var query = db.Games.Include(g => g.OwnerUser).AsQueryable();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var normalized = Search.Trim();
            query = query.Where(g =>
                EF.Functions.ILike(g.Name, $"%{normalized}%") || g.Id.ToString() == normalized
            );
        }

        var paged = await query
            .OrderByDescending(g => g.LastActiveAt)
            .ToPagedResultAsync(PageNumber, PageSize);
        Games = paged.Items;
        Pager = paged.Pager;
    }
}
