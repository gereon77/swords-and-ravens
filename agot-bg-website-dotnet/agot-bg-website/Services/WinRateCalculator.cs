namespace agot_bg_website.Services;

/// <summary>
/// Minimal facts needed to compute win rate for one user, decoupled from EF entities so this is
/// trivially unit-testable. See MIGRATION_PLAN.md §10.2 for the formula this implements.
/// </summary>
public record WinRateGameFact(bool IsFinished, bool IsWinner);

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
/// </summary>
public static class WinRateCalculator
{
    public static WinRateResult Calculate(
        IEnumerable<WinRateGameFact> currentParticipations,
        int finishedGamesPlayerWasRemovedFromCount)
    {
        if (finishedGamesPlayerWasRemovedFromCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(finishedGamesPlayerWasRemovedFromCount));
        }

        var finished = currentParticipations.Where(g => g.IsFinished).ToList();

        var wins = finished.Count(g => g.IsWinner);
        var losses = finished.Count(g => !g.IsWinner) + finishedGamesPlayerWasRemovedFromCount;

        return new WinRateResult(wins, losses);
    }
}
