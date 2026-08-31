using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Pages;

/// <summary>
/// "All games" — a public list of every in-progress/joinable game, mirroring Django's
/// agotboardgame_main.views.games() (MIGRATION_PLAN.md notes it lived at /games).
/// </summary>
public class GamesModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager) : PageModel
{
    public List<Game> OpenGames { get; set; } = [];

    public List<Game> OngoingGames { get; set; } = [];

    public bool CanCreateGame { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        CanCreateGame = RoleNames.CanCreateGame(User);

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

    public async Task<IActionResult> OnPostCreateGameAsync([FromForm] string name)
    {
        if (!RoleNames.CanCreateGame(User))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
        {
            ErrorMessage = "Game name must be between 1 and 200 characters.";
            return RedirectToPage();
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var game = new Game
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            OwnerUserId = user.Id,
            State = GameState.InLobby,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow
        };

        db.Games.Add(game);
        await db.SaveChangesAsync();

        return Redirect($"/play/{game.Id}");
    }
}
