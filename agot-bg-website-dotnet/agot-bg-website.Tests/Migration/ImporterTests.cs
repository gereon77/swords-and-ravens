using System.Text.Json;
using agot_bg_website.Domain;
using Snr.Migration;
using Xunit;

namespace agot_bg_website.Tests.Migration;

/// <summary>
/// Snr.Migration is a standalone console app (see MIGRATION_PLAN.md §10), so its tests live here
/// alongside the rest of the solution's unit tests rather than in a fourth test project.
/// </summary>
public class ImporterTests
{
    [Theory]
    [InlineData("IN_LOBBY", GameState.InLobby)]
    [InlineData("ONGOING", GameState.Ongoing)]
    [InlineData("FINISHED", GameState.Finished)]
    [InlineData("CANCELLED", GameState.Cancelled)]
    public void ParseGameState_MapsKnownLegacyStates(string legacyState, GameState expected)
    {
        Assert.Equal(expected, Importer.ParseGameState(legacyState));
    }

    [Fact]
    public void ParseGameState_FallsBackToInLobbyForUnknownState()
    {
        // Covers both a genuinely unrecognized future value and CLOSED, a state Django's
        // agotboardgame_main.models defined (models.py:16) but never actually assigned to any
        // game (confirmed: no legacy code ever set game.state = CLOSED) - so no real imported data
        // can contain it, but this still documents the fallback in case that assumption is wrong.
        Assert.Equal(
            GameState.InLobby,
            Importer.ParseGameState("SOME_FUTURE_STATE_WE_DONT_KNOW_YET")
        );
        Assert.Equal(GameState.InLobby, Importer.ParseGameState("CLOSED"));
    }

    [Theory]
    [InlineData("""{"turn": -1}""", true)]
    [InlineData("""{"turn": 0}""", false)]
    [InlineData("""{"turn": 12}""", false)]
    [InlineData("""{}""", false)]
    [InlineData(null, false)]
    public void IsTurnMinusOne_ReadsTheTurnFieldOfAnAlreadyParsedViewOfGameDocument(
        string? json,
        bool expected
    )
    {
        using var doc = json is null ? null : JsonDocument.Parse(json);
        Assert.Equal(expected, Importer.IsTurnMinusOne(doc));
    }
}
