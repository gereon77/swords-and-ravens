using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Areas.Admin.Pages.Games;

public class IndexModel(ApplicationDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public List<Game> Games { get; set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        var query = db.Games.Include(g => g.OwnerUser).AsQueryable();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var normalized = Search.Trim();
            query = query.Where(g =>
                EF.Functions.ILike(g.Name, $"%{normalized}%") ||
                g.Id.ToString() == normalized);
        }

        Games = await query.OrderByDescending(g => g.LastActiveAt).Take(100).ToListAsync();
    }
}
