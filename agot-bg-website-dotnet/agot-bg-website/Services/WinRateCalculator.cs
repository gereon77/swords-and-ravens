namespace agot_bg_website.Services;

/// <summary>
/// Minimal facts needed to compute win rate for one user, decoupled from EF entities so this is
/// trivially unit-testable. See MIGRATION_PLAN.md §10.2 for the formula this implements.
/// </summary>
/// <summary><paramref name="IsReplacer"/> is true when this user jumped into the game as a
/// replacer (their id is in that game's `ViewOfGame.replacerIds`) - see <see
/// cref="WinRateCalculator"/>'s doc comment for how it affects the formula.</summary>
public record WinRateGameFact(bool IsFinished, bool IsWinner, bool IsReplacer = false);

public record WinRateResult(int Wins, int Losses)
{
    public int TotalGames => Wins + Losses;

    public double? WinRate => TotalGames == 0 ? null : (double)Wins / TotalGames;
}

/// <summary>
/// Computes a user's win rate from their PlayerInGame rows (current, still-in-the-game
/// participations) and PreviousPlayerInGame rows (participations that ended early because the
/// player was replaced by a vassal/other player, or timed out).
///
/// Per an explicit product decision (MIGRATION_PLAN.md §10.2): every PreviousPlayerInGame row for
/// a FINISHED game counts as a loss unconditionally, regardless of whether the house the player
/// was removed from went on to win — being removed from a game should never count in your favor.
/// The one exception is a removal from a game the user had joined as a replacer themselves: the
/// caller (<see cref="UserStatsService.RecalculateAsync"/>) already excludes that count entirely
/// before it ever reaches <paramref name="finishedGamesPlayerWasRemovedFromCount"/>, for the same
/// "no downside risk" reason as the replacer-loss exclusion below.
///
/// A further, separate product decision covers games joined as a replacer (seated into an
/// existing house via a player-replacement vote, mid-game - see <see
/// cref="WinRateGameFact.IsReplacer"/>): a loss in one of these is excluded from both the
/// numerator and denominator entirely (it neither helps nor hurts the percentage), while a win
/// still counts normally. This is meant to encourage players to help finish games that would
/// otherwise stall on a missing player, without adding downside risk to their win rate.
/// </summary>
public static class WinRateCalculator
{
    public static WinRateResult Calculate(
        IEnumerable<WinRateGameFact> currentParticipations,
        int finishedGamesPlayerWasRemovedFromCount
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(finishedGamesPlayerWasRemovedFromCount);

        var finished = currentParticipations.Where(g => g.IsFinished).ToList();

        var wins = finished.Count(g => g.IsWinner);
        var losses =
            finished.Count(g => !g.IsWinner && !g.IsReplacer)
            + finishedGamesPlayerWasRemovedFromCount;

        return new WinRateResult(wins, losses);
    }
}
