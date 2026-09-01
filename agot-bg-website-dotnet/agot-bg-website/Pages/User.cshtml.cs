using System.Globalization;
using System.Text.Json;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure;
using agot_bg_website.Infrastructure.Auth;
using agot_bg_website.Services;
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
public class UserModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IAuthorizationService authorizationService) : PageModel
{
    /// <summary>Badge color per role, mirroring Django's settings.GROUP_COLORS (bootstrap contextual
    /// names mapped onto their DaisyUI badge-* equivalents).</summary>
    private static readonly Dictionary<string, string> GroupBadgeClasses = new()
    {
        [RoleNames.Admin] = "badge-error",
        [RoleNames.HighMember] = "badge-info",
        [RoleNames.Banned] = "badge-error",
        [RoleNames.OnProbation] = "badge-warning",
        [RoleNames.Tongueless] = "badge-warning"
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
        string? Winner);

    public ApplicationUser ViewedUser { get; set; } = null!;

    public List<(string Name, string BadgeClass)> UserGroups { get; set; } = [];

    public bool IsOwnProfile { get; set; }

    public bool OnProbation { get; set; }

    public bool CanPlayAsAnotherPlayer { get; set; }

    public List<GameRow> GamesOfUser { get; set; } = [];

    public List<GameRow> CancelledGames { get; set; } = [];

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
        IsOwnProfile = currentUserId is not null && Guid.TryParse(currentUserId, out var currentUserGuid) && currentUserGuid == id;
        OnProbation = User.IsInRole(RoleNames.OnProbation);
        CanPlayAsAnotherPlayer = (await authorizationService.AuthorizeAsync(User, GamePermissions.ImpersonateOtherPlayers)).Succeeded;

        var viewedUserRoles = await userManager.GetRolesAsync(viewedUser);
        UserGroups = GroupBadgeClasses
            .Where(kv => viewedUserRoles.Contains(kv.Key))
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

        await LoadGamesAsync(id);
        await LoadStatsAsync(id);

        return Page();
    }

    private async Task LoadGamesAsync(Guid userId)
    {
        var playerRows = await db.PlayersInGame
            .Include(p => p.Game)
            .ThenInclude(g => g!.Players)
            .Where(p => p.UserId == userId && p.Game != null && p.Game.ViewOfGame != null)
            .OrderByDescending(p => p.Game!.CreatedAt)
            .ToListAsync();

        foreach (var row in playerRows)
        {
            var game = row.Game!;
            var view = game.ViewOfGame!.RootElement;

            var settings = view.TryGetProperty("settings", out var settingsElement) && settingsElement.ValueKind == JsonValueKind.Object
                ? settingsElement
                : (JsonElement?)null;

            // A faceless game hides who's playing which house entirely - Django excludes these
            // from the profile's games list outright rather than showing misleading data.
            var isFaceless = settings?.TryGetProperty("faceless", out var facelessElement) == true && facelessElement.ValueKind == JsonValueKind.True;
            if (isFaceless)
            {
                continue;
            }

            var setupId = settings?.TryGetProperty("setupId", out var setupIdElement) == true && setupIdElement.ValueKind == JsonValueKind.String
                ? setupIdElement.GetString()
                : null;
            var isLearnTheGame = setupId == "learn-the-game";

            var maxPlayerCount = view.TryGetProperty("maxPlayerCount", out var maxPlayerCountElement) && maxPlayerCountElement.ValueKind == JsonValueKind.Number
                ? maxPlayerCountElement.GetInt32()
                : (int?)null;
            var turn = view.TryGetProperty("turn", out var turnElement) && turnElement.ValueKind == JsonValueKind.Number
                ? turnElement.GetInt32()
                : (int?)null;
            var waitingFor = view.TryGetProperty("waitingFor", out var waitingForElement) && waitingForElement.ValueKind == JsonValueKind.String
                ? waitingForElement.GetString()
                : null;
            var winner = view.TryGetProperty("winner", out var winnerElement) && winnerElement.ValueKind == JsonValueKind.String
                ? winnerElement.GetString()
                : null;

            string? house = null;
            bool? isWinner = null;
            if (row.Data is not null)
            {
                var data = row.Data.RootElement;
                house = data.TryGetProperty("house", out var houseElement) && houseElement.ValueKind == JsonValueKind.String
                    ? houseElement.GetString()
                    : null;
                if (data.TryGetProperty("is_winner", out var isWinnerElement) &&
                    (isWinnerElement.ValueKind == JsonValueKind.True || isWinnerElement.ValueKind == JsonValueKind.False))
                {
                    isWinner = isWinnerElement.GetBoolean();
                }
            }

            var gameRow = new GameRow(
                game.Id, game.Name, game.State, house, game.Players.Count, maxPlayerCount, isWinner,
                game.CreatedAt, game.LastActiveAt, turn, waitingFor, winner);

            if (game.State == GameState.Cancelled)
            {
                CancelledGames.Add(gameRow);
                continue;
            }

            if (game.State is not (GameState.InLobby or GameState.Ongoing or GameState.Finished))
            {
                continue;
            }

            GamesOfUser.Add(gameRow);

            if (game.State == GameState.Ongoing)
            {
                OngoingCount++;
            }

            // A row only counts towards win-rate stats once it's actually finished with a
            // recorded outcome and isn't the "learn the game" tutorial variant - see
            // MIGRATION_PLAN.md §10.2 and Django's identical exclusions in user_profile().
            var countsTowardsStats = game.State == GameState.Finished && !isLearnTheGame && isWinner.HasValue;
            _winRateFacts.Add(new WinRateGameFact(IsFinished: countsTowardsStats, IsWinner: isWinner == true));
        }
    }

    private readonly List<WinRateGameFact> _winRateFacts = [];

    private async Task LoadStatsAsync(Guid userId)
    {
        RemovedFromGameCount = await db.PreviousPlayersInGame
            .Where(p => p.UserId == userId && p.Game!.State == GameState.Finished)
            .CountAsync();

        var winRate = WinRateCalculator.Calculate(_winRateFacts, RemovedFromGameCount);
        WonCount = winRate.Wins;
        FinishedCount = winRate.TotalGames;
        WinRateDisplay = winRate.WinRate.HasValue
            ? $"{(winRate.WinRate.Value * 100).ToString("F1", CultureInfo.InvariantCulture)} %"
            : "n/a";

        var responseTimes = await db.PbemResponseTimes
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(100)
            .Select(p => p.ResponseTime)
            .ToListAsync();
        var average = PbemResponseTimeCalculator.CalculateAverage(responseTimes);
        AveragePbemResponseTimeDisplay = average.HasValue ? FormatTimeSpan(average.Value) : "n/a";
    }

    private static string FormatTimeSpan(TimeSpan span)
    {
        return span.Days > 0
            ? $"{span.Days}d {span.Hours}h {span.Minutes}m"
            : $"{(int)span.TotalHours}h {span.Minutes}m {span.Seconds}s";
    }
}
