using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Auth;
using agot_bg_website.Services.GameListing;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace agot_bg_website.Pages;

/// <summary>
/// "All games" — a public list of every in-progress/joinable game, mirroring Django's
/// agotboardgame_main.views.games() (MIGRATION_PLAN.md notes it lived at /games). Every list here
/// is built from <see cref="GameListQueryService"/>, which never loads Game.SerializedGame - see
/// its doc comment.
/// </summary>
public class GamesModel(ApplicationDbContext db, GameListQueryService gameLists, UserManager<ApplicationUser> userManager) : PageModel
{
    public List<GameListItem> MyGames { get; set; } = [];

    public List<GameListItem> OpenGames { get; set; } = [];

    public List<GameListItem> OngoingGames { get; set; } = [];

    public List<GameListItem> InactiveGames { get; set; } = [];

    public List<GameListItem> ReplacementNeededGames { get; set; } = [];

    public List<GameListItem> InactiveTournamentGames { get; set; } = [];

    public List<GameListItem> InactivePrivateGames { get; set; } = [];

    public bool CanCreateGame { get; set; }

    public bool CanPlayAsAnotherPlayer { get; set; }

    public bool CanCancelGame { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        CanCreateGame = RoleNames.CanCreateGame(User);
        CanPlayAsAnotherPlayer = RoleNames.CanPlayAsAnotherPlayer.Any(User.IsInRole);
        CanCancelGame = User.IsInRole(RoleNames.Admin);

        var userId = userManager.GetUserId(User);
        var viewerId = userId is not null ? Guid.Parse(userId) : (Guid?)null;

        OpenGames = await gameLists.GetOpenGamesAsync();
        OngoingGames = await gameLists.GetOngoingGamesAsync();
        ReplacementNeededGames = await gameLists.GetReplacementNeededGamesAsync(viewerId);

        if (viewerId is not null)
        {
            MyGames = await gameLists.GetMyGamesAsync(viewerId.Value);
        }

        if (CanPlayAsAnotherPlayer)
        {
            InactiveGames = await gameLists.GetInactiveGamesAsync(viewerId);
            InactiveTournamentGames = await gameLists.GetInactiveTournamentGamesAsync();
        }

        if (CanCancelGame)
        {
            InactivePrivateGames = await gameLists.GetInactivePrivateGamesAsync();
        }
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
