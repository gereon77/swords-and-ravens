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
public class MyGamesModel(
    ApplicationDbContext db,
    GameListQueryService gameLists,
    UserManager<ApplicationUser> userManager,
    IAuthorizationService authorizationService,
    ILogger<MyGamesModel> logger
) : PageModel
{
    public List<GameListItem> MyGames { get; set; } = [];

    public List<GameListItem> CurrentLiveGames { get; set; } = [];

    public bool CanCreateGame { get; set; }

    public bool CanPlayAsAnotherPlayer { get; set; }

    public bool CanCancelGame { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        CanCreateGame = (
            await authorizationService.AuthorizeAsync(User, GamePermissions.CreateGame)
        ).Succeeded;
        CanPlayAsAnotherPlayer = (
            await authorizationService.AuthorizeAsync(User, GamePermissions.ImpersonateOtherPlayers)
        ).Succeeded;
        CanCancelGame = (
            await authorizationService.AuthorizeAsync(User, GamePermissions.CancelGame)
        ).Succeeded;

        CurrentLiveGames = await gameLists.GetCurrentLiveGamesAsync();

        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return;
        }

        MyGames = await gameLists.GetMyGamesAsync(Guid.Parse(userId));
    }

    public async Task<IActionResult> OnPostCreateGameAsync([FromForm] string name)
    {
        if (
            !(await authorizationService.AuthorizeAsync(User, GamePermissions.CreateGame)).Succeeded
        )
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
            LastActiveAt = DateTimeOffset.UtcNow,
        };

        db.Games.Add(game);
        await db.SaveChangesAsync();

        return Redirect($"/play/{game.Id}");
    }

    /// <summary>
    /// Same behavior as <see cref="GamesModel.OnPostCancelGameAsync"/> - the "Current live games"
    /// list on this page needs its own Cancel button target since Razor Pages page handlers are
    /// per-page, not shared with Games.cshtml.
    /// </summary>
    public async Task<IActionResult> OnPostCancelGameAsync([FromForm] Guid gameId)
    {
        if (
            !(await authorizationService.AuthorizeAsync(User, GamePermissions.CancelGame)).Succeeded
        )
        {
            return Forbid();
        }

        var game = await db.Games.FindAsync(gameId);
        if (game is not null)
        {
            game.State = GameState.Cancelled;
            game.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            logger.LogInformation(
                "{Username} ({UserId}) cancelled game {GameName} ({GameId})",
                User.Identity?.Name,
                userManager.GetUserId(User),
                game.Name,
                game.Id
            );
        }

        return RedirectToPage();
    }
}
