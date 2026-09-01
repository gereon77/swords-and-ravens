using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Auth;
using agot_bg_website.Services.GameListing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace agot_bg_website.Pages;

/// <summary>
/// "My games" — every open/ongoing game the signed-in user is a player in, mirroring Django's
/// agotboardgame_main.views.my_games() (MIGRATION_PLAN.md notes it lived at /my_games). Uses
/// <see cref="GameListQueryService"/>, which never loads Game.SerializedGame - see its doc comment.
/// </summary>
[Authorize]
public class MyGamesModel(ApplicationDbContext db, GameListQueryService gameLists, UserManager<ApplicationUser> userManager) : PageModel
{
    public List<GameListItem> MyGames { get; set; } = [];

    public bool CanCreateGame { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        CanCreateGame = RoleNames.CanCreateGame(User);

        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return;
        }

        MyGames = await gameLists.GetMyGamesAsync(Guid.Parse(userId));
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
