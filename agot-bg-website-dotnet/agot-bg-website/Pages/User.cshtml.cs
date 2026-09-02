using System.Globalization;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure;
using agot_bg_website.Infrastructure.Auth;
using agot_bg_website.Infrastructure.Stats;
using agot_bg_website.Services;
using agot_bg_website.Services.GameListing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Pages;

/// <summary>
/// Public user profile, mirroring Django's agotboardgame_main.views.user_profile (route
/// "/user/&lt;uuid:user_id&gt;", template user_profile.html) — see MIGRATION_PLAN.md §10.2/§13/§14.
/// </summary>
public class UserModel(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IAuthorizationService authorizationService,
    WinRateRecalculationQueue winRateQueue
) : PageModel
{
    /// <summary>Badge color per role, mirroring Django's settings.GROUP_COLORS (bootstrap contextual
    /// names mapped onto their DaisyUI badge-* equivalents).</summary>
    private static readonly Dictionary<string, string> GroupBadgeClasses = new()
    {
        [RoleNames.Admin] = "badge-error",
        [RoleNames.HighMember] = "badge-info",
        [RoleNames.Banned] = "badge-error",
        [RoleNames.OnProbation] = "badge-warning",
        [RoleNames.Tongueless] = "badge-warning",
    };

    public record GameRow(
        Guid GameId,
        string Name,
        GameState State,
        string? House,
        int PlayersCount,
        int? MaxPlayerCount,
        bool? IsWinner,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastActiveAt,
        int? Turn,
        string? WaitingFor,
        string? Winner
    );

    /// <summary>A game the viewed user was removed from (voted out/timed out) before it ended,
    /// per <c>PreviousPlayerInGame</c> - never shown anywhere else in the UI, see
    /// MIGRATION_PLAN.md §10.2's "games where you were removed" follow-up.</summary>
    public record PreviouslyParticipatedGameRow(
        Guid GameId,
        string Name,
        GameState State,
        int PlayersCount,
        int? MaxPlayerCount,
        DateTimeOffset? ReplacedAt,
        PlayerReplacementReason? Reason
    );

    public ApplicationUser ViewedUser { get; set; } = null!;

    public List<(string Name, string BadgeClass)> UserGroups { get; set; } = [];

    public bool IsOwnProfile { get; set; }

    public bool OnProbation { get; set; }

    public bool CanPlayAsAnotherPlayer { get; set; }

    public List<GameRow> GamesOfUser { get; set; } = [];

    public List<GameRow> CancelledGames { get; set; } = [];

    public List<PreviouslyParticipatedGameRow> PreviouslyParticipatedGames { get; set; } = [];

    public int OngoingCount { get; set; }

    public int FinishedCount { get; set; }

    public int WonCount { get; set; }

    public int RemovedFromGameCount { get; set; }

    public string WinRateDisplay { get; set; } = "n/a";

    public string AveragePbemResponseTimeDisplay { get; set; } = "n/a";

    public string RelativeLastActivity => RelativeTimeFormatter.Format(ViewedUser.LastActivity);

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var viewedUser = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (viewedUser is null || viewedUser.IsDeleted)
        {
            // Deleted ("Took the Black") accounts 404 - see MIGRATION_PLAN.md §13/§14.
            return NotFound();
        }

        ViewedUser = viewedUser;

        var currentUserId = userManager.GetUserId(User);
        IsOwnProfile =
            currentUserId is not null
            && Guid.TryParse(currentUserId, out var currentUserGuid)
            && currentUserGuid == id;
        OnProbation = User.IsInRole(RoleNames.OnProbation);
        CanPlayAsAnotherPlayer = (
            await authorizationService.AuthorizeAsync(User, GamePermissions.ImpersonateOtherPlayers)
        ).Succeeded;

        var viewedUserRoles = await userManager.GetRolesAsync(viewedUser);
        UserGroups = GroupBadgeClasses
            .Where(kv => viewedUserRoles.Contains(kv.Key))
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

        await LoadGamesAsync(id);
        await LoadPreviouslyParticipatedGamesAsync(id);
        await LoadStatsAsync(id);

        return Page();
    }

    /// <summary>
    /// Loads every game the viewed user has ever played for the games/cancelled-games tables.
    /// Deliberately projects only the handful of scalar/ViewOfGame columns the page actually
    /// needs instead of `.Include(p => p.Game)`-ing the full <c>Game</c> entity: an `Include`
    /// pulls every column, including `SerializedGame` (a potentially multi-megabyte JSON blob per
    /// game), for every game a user has ever played - hundreds for a long-time user - which was
    /// the entire reason this page loaded dramatically slower than Django's equivalent (which
    /// used `.defer('serialized_game')`). See GameListQueryService.Project() for the same
    /// established pattern elsewhere in this codebase.
    /// </summary>
    private async Task LoadGamesAsync(Guid userId)
    {
        var playerRows = await db
            .PlayersInGame.Where(p => p.UserId == userId && p.Game != null && p.Game.ViewOfGame != null)
            .OrderByDescending(p => p.Game!.CreatedAt)
            .Select(p => new
            {
                p.Data,
                p.Game!.Id,
                p.Game.Name,
                p.Game.State,
                p.Game.ViewOfGame,
                p.Game.CreatedAt,
                p.Game.LastActiveAt,
                PlayersCount = p.Game.Players.Count,
            })
            .ToListAsync();

        foreach (var row in playerRows)
        {
            var view = ViewOfGameInfo.Parse(row.ViewOfGame);

            // A faceless game hides who's playing which house entirely - Django excludes these
            // from the profile's games list outright rather than showing misleading data.
            if (view.IsFaceless)
            {
                continue;
            }

            var winner = GetStringProperty(row.ViewOfGame, "winner");
            var player = PlayerInGameInfo.Parse(row.Data);

            var gameRow = new GameRow(
                row.Id,
                row.Name,
                row.State,
                player.House,
                row.PlayersCount,
                view.MaxPlayerCount,
                player.IsWinner,
                row.CreatedAt,
                row.LastActiveAt,
                view.Turn,
                view.WaitingFor,
                winner
            );

            if (row.State == GameState.Cancelled)
            {
                CancelledGames.Add(gameRow);
                continue;
            }

            if (row.State is not (GameState.InLobby or GameState.Ongoing or GameState.Finished))
            {
                continue;
            }

            GamesOfUser.Add(gameRow);

            if (row.State == GameState.Ongoing)
            {
                OngoingCount++;
            }
        }
    }

    /// <summary>
    /// Loads games the viewed user was removed from (voted out/timed out) before they ended - see
    /// <see cref="PreviouslyParticipatedGameRow"/>'s doc comment. Same no-SerializedGame
    /// projection discipline as <see cref="LoadGamesAsync"/>.
    /// </summary>
    private async Task LoadPreviouslyParticipatedGamesAsync(Guid userId)
    {
        // Filtered to Game.State == Finished to match RemovedFromGameCount/Replaced-left-early's
        // exact same filter (LoadStatsAsync via UserStatsService) - a removal from a game that was
        // later cancelled must never count towards, or even appear alongside, that stat (cancelled
        // games never affect stats at all - MIGRATION_PLAN.md §10.2), so it's dropped here too
        // rather than only from the cached count.
        var rows = await db
            .PreviousPlayersInGame.Where(p =>
                p.UserId == userId && p.Game != null && p.Game.State == GameState.Finished
            )
            .OrderByDescending(p => p.ReplacedAt)
            .Select(p => new
            {
                p.ReplacedAt,
                p.Reason,
                p.Game!.Id,
                p.Game.Name,
                p.Game.State,
                p.Game.ViewOfGame,
                PlayersCount = p.Game.Players.Count,
            })
            .ToListAsync();

        PreviouslyParticipatedGames = rows.Select(row => new PreviouslyParticipatedGameRow(
                row.Id,
                row.Name,
                row.State,
                row.PlayersCount,
                ViewOfGameInfo.Parse(row.ViewOfGame).MaxPlayerCount,
                row.ReplacedAt,
                row.Reason
            ))
            .ToList();
    }

    private async Task LoadStatsAsync(Guid userId)
    {
        // Prefer the cached stats UserStatsService keeps up to date in the background whenever a
        // game finishes (see Api.GamesApi's PATCH handler) over recomputing from every
        // PlayerInGame/PreviousPlayerInGame row on every single profile view. StatsCachedAt is
        // only ever null for a user whose stats have genuinely never been computed yet (i.e.
        // every pre-existing user right after this feature ships, before their next game
        // finishes) - rather than computing synchronously here (which would reintroduce the same
        // "load everything on every profile view" cost this page was just fixed to avoid), just
        // enqueue it for the background service to pick up and show "n/a"/0 for this one request;
        // the next profile view (this user's own, or anyone else's) will see the cached numbers.
        if (ViewedUser.StatsCachedAt is not null)
        {
            WonCount = ViewedUser.CachedWonGamesCount ?? 0;
            FinishedCount = ViewedUser.CachedFinishedGamesCount ?? 0;
            RemovedFromGameCount = ViewedUser.CachedRemovedFromGameCount ?? 0;
            WinRateDisplay = ViewedUser.CachedWinRate.HasValue
                ? $"{(ViewedUser.CachedWinRate.Value * 100).ToString("F1", CultureInfo.InvariantCulture)} %"
                : "n/a";
        }
        else
        {
            winRateQueue.Enqueue(userId);
        }

        var responseTimes = await db
            .PbemResponseTimes.Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(100)
            .Select(p => p.ResponseTime)
            .ToListAsync();
        var average = PbemResponseTimeCalculator.CalculateAverage(responseTimes);
        AveragePbemResponseTimeDisplay = average.HasValue ? FormatTimeSpan(average.Value) : "n/a";
    }

    private static string? GetStringProperty(System.Text.Json.JsonDocument? doc, string name)
    {
        if (doc is null)
        {
            return null;
        }

        var root = doc.RootElement;
        return root.TryGetProperty(name, out var element)
            && element.ValueKind == System.Text.Json.JsonValueKind.String
            ? element.GetString()
            : null;
    }

    private static string FormatTimeSpan(TimeSpan span)
    {
        return span.Days > 0
            ? $"{span.Days}d {span.Hours}h {span.Minutes}m"
            : $"{(int)span.TotalHours}h {span.Minutes}m {span.Seconds}s";
    }
}

