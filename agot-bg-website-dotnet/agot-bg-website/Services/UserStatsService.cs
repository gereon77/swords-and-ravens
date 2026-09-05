using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Services.GameListing;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Services;

/// <summary>Result of a stats recalculation - see <see cref="UserStatsService.RecalculateAsync"/>.
/// <paramref name="FinishedGamesCount"/> only considers games actually in state Finished (minus
/// faceless games, which the games list hides entirely too) - it deliberately excludes games left
/// early (<paramref name="RemovedFromGameCount"/>) and never includes cancelled games, so it
/// always reconciles with "Ongoing + Finished" on the profile's games list. <paramref
/// name="RemovedFromGameCount"/> counts a removal as soon as it happens, whether the game has
/// since finished or is still Ongoing - a player who was voted out/timed out doesn't get a pass
/// just because nobody's won yet. <paramref name="WinRate"/>'s denominator is the only place
/// left-early games get folded in (always as a loss), and it further excludes the tutorial
/// variant and any row without a recorded outcome - see <see cref="WinRateCalculator"/>.</summary>
public record UserStatsResult(
    int WonGamesCount,
    int FinishedGamesCount,
    int RemovedFromGameCount,
    double? WinRate
);

/// <summary>
/// Computes and persists a user's cached win-rate stats (<see
/// cref="ApplicationUser.CachedWinRate"/> and friends), reusing the exact same facts/formula
/// <see cref="WinRateCalculator"/> already implements - see that type's doc comment and
/// MIGRATION_PLAN.md §10.2.
///
/// Called from two places: <see cref="Infrastructure.Stats.UserStatsRecalculationBackgroundService"/>
/// in the background whenever a game finishes (see Api.GamesApi's PATCH handler, which enqueues
/// every participant), and synchronously from Pages.UserModel as a one-time fallback for any user
/// whose stats have never been cached yet (<see cref="ApplicationUser.StatsCachedAt"/> is null).
///
/// Deliberately never touches Game.SerializedGame - only PlayerInGame.Data/Game.ViewOfGame/State,
/// mirroring the same "no full Game entity load" discipline as
/// GameListQueryService/UserModel.LoadGamesAsync.
/// </summary>
public sealed class UserStatsService(ApplicationDbContext db)
{
    public async Task<UserStatsResult?> RecalculateAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    )
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            // Deleted between being enqueued and the background service picking it up - nothing
            // to cache stats for.
            return null;
        }

        var participationRows = await db
            .PlayersInGame.Where(p => p.UserId == userId && p.Game != null)
            .Select(p => new
            {
                p.Data,
                State = p.Game!.State,
                p.Game.ViewOfGame,
            })
            .ToListAsync(cancellationToken);

        // Faceless games hide who's playing which house entirely, so Pages.UserModel's games
        // list (GamesOfUser/CancelledGames) excludes them outright - see UserModel.LoadGamesAsync.
        // "Finished games" below must line up with that same list (Games badge = Ongoing +
        // Finished, always), so it applies the identical faceless exclusion rather than only the
        // narrower "has a recorded is_winner and isn't the tutorial" exclusion win-rate needs.
        var nonFacelessRows = participationRows
            .Where(row => !ViewOfGameInfo.Parse(row.ViewOfGame).IsFaceless)
            .ToList();

        var winRateFacts = nonFacelessRows
            .Select(row =>
            {
                var isLearnTheGame = ViewOfGameInfo.Parse(row.ViewOfGame).IsLearnTheGame;
                var isWinner = PlayerInGameInfo.Parse(row.Data).IsWinner;

                // A row only counts towards the win-rate percentage once it's actually finished
                // with a recorded outcome and isn't the "learn the game" tutorial variant - see
                // MIGRATION_PLAN.md §10.2 and Django's identical exclusions in user_profile().
                // Cancelled/Ongoing/InLobby games never reach here as "finished" (they simply
                // don't set countsTowardsWinRate), matching the product rule that cancelled games
                // must never affect stats at all.
                var countsTowardsWinRate =
                    row.State == GameState.Finished && !isLearnTheGame && isWinner.HasValue;
                return new WinRateGameFact(
                    IsFinished: countsTowardsWinRate,
                    IsWinner: isWinner == true
                );
            })
            .ToList();

        // A removal always counts as a loss regardless of whether the game has finished yet - a
        // player voted out/timed out of a still-Ongoing game doesn't get a pass just because
        // nobody's declared a winner yet. Only Cancelled (and InLobby, though a removal can't
        // happen there) games are excluded, per "cancelled games never affect any stat at all".
        // The tutorial variant is excluded here too, for the same reason it's excluded from the
        // win side above - a "learn the game" removal must not count as a loss either.
        var removedFromGameViewsOfGame = await db
            .PreviousPlayersInGame.Where(p =>
                p.UserId == userId
                && (p.Game!.State == GameState.Finished || p.Game.State == GameState.Ongoing)
            )
            .Select(p => p.Game!.ViewOfGame)
            .ToListAsync(cancellationToken);
        var removedFromGameCount = removedFromGameViewsOfGame.Count(viewOfGame =>
            !ViewOfGameInfo.Parse(viewOfGame).IsLearnTheGame
        );

        var winRate = WinRateCalculator.Calculate(winRateFacts, removedFromGameCount);

        // "Finished games" (as shown on the profile page) must only consider games actually in
        // state Finished - it deliberately does NOT exclude the tutorial/no-recorded-outcome rows
        // the win-rate percentage above excludes, and does NOT include PreviousPlayerInGame rows
        // (games left early) either, so this always reconciles with Games = Ongoing + Finished on
        // the games list itself.
        var finishedGamesCount = nonFacelessRows.Count(row => row.State == GameState.Finished);

        user.CachedWonGamesCount = winRate.Wins;
        user.CachedFinishedGamesCount = finishedGamesCount;
        user.CachedRemovedFromGameCount = removedFromGameCount;
        user.CachedWinRate = winRate.WinRate;
        user.StatsCachedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return new UserStatsResult(
            winRate.Wins,
            finishedGamesCount,
            removedFromGameCount,
            winRate.WinRate
        );
    }
}
