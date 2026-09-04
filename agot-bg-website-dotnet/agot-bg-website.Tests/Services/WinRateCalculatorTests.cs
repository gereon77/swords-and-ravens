using agot_bg_website.Services;
using Xunit;

namespace agot_bg_website.Tests.Services;

public class WinRateCalculatorTests
{
    [Fact]
    public void NoGames_ReturnsNullWinRate()
    {
        var result = WinRateCalculator.Calculate([], finishedGamesPlayerWasRemovedFromCount: 0);

        Assert.Equal(0, result.Wins);
        Assert.Equal(0, result.Losses);
        Assert.Null(result.WinRate);
    }

    [Fact]
    public void OnlyFinishedGamesCount()
    {
        var facts = new[]
        {
            new WinRateGameFact(IsFinished: true, IsWinner: true),
            new WinRateGameFact(IsFinished: true, IsWinner: false),
            new WinRateGameFact(IsFinished: false, IsWinner: true), // ongoing — must be ignored
        };

        var result = WinRateCalculator.Calculate(facts, finishedGamesPlayerWasRemovedFromCount: 0);

        Assert.Equal(1, result.Wins);
        Assert.Equal(1, result.Losses);
        Assert.Equal(0.5, result.WinRate);
    }

    [Fact]
    public void RemovedFromGame_AlwaysCountsAsLoss_RegardlessOfCurrentParticipations()
    {
        // See MIGRATION_PLAN.md §10.2: PreviousPlayerInGame rows always count as losses,
        // even if the player also won some other finished game.
        var facts = new[] { new WinRateGameFact(IsFinished: true, IsWinner: true) };

        var result = WinRateCalculator.Calculate(facts, finishedGamesPlayerWasRemovedFromCount: 3);

        Assert.Equal(1, result.Wins);
        Assert.Equal(3, result.Losses);
        Assert.Equal(4, result.TotalGames);
        Assert.Equal(0.25, result.WinRate);
    }

    [Fact]
    public void NegativeRemovedFromGameCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WinRateCalculator.Calculate([], finishedGamesPlayerWasRemovedFromCount: -1)
        );
    }
}
