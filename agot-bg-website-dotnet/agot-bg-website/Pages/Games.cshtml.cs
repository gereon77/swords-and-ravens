using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Pages;

/// <summary>
/// "All games" — a public list of every in-progress/joinable game, mirroring Django's
/// agotboardgame_main.views.games() (MIGRATION_PLAN.md notes it lived at /games).
/// </summary>
public class GamesModel(ApplicationDbContext db) : PageModel
{
    public List<Game> OpenGames { get; set; } = [];

    public List<Game> OngoingGames { get; set; } = [];

    public async Task OnGetAsync()
    {
        OpenGames = await db.Games
            .Include(g => g.OwnerUser)
            .Include(g => g.Players)
            .Where(g => g.State == GameState.InLobby)
            .OrderByDescending(g => g.CreatedAt)
            .Take(100)
            .ToListAsync();

        OngoingGames = await db.Games
            .Include(g => g.OwnerUser)
            .Include(g => g.Players)
            .Where(g => g.State == GameState.Ongoing)
            .OrderByDescending(g => g.LastActiveAt)
            .Take(100)
            .ToListAsync();
    }
}
