using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Pages;

/// <summary>
/// "My games" — every game the signed-in user is (or was) a player in, mirroring Django's
/// agotboardgame_main.views.my_games() (MIGRATION_PLAN.md notes it lived at /my_games).
/// </summary>
[Authorize]
public class MyGamesModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager) : PageModel
{
    public List<Game> MyGames { get; set; } = [];

    public async Task OnGetAsync()
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return;
        }

        var userGuid = Guid.Parse(userId);
        MyGames = await db.Games
            .Include(g => g.OwnerUser)
            .Include(g => g.Players)
            .Where(g => g.Players.Any(p => p.UserId == userGuid))
            .OrderByDescending(g => g.LastActiveAt)
            .ToListAsync();
    }
}
