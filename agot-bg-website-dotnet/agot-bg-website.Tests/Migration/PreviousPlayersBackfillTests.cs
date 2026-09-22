using System.Text.Json;
using agot_bg_website.Domain;
using Snr.Migration;
using Xunit;

namespace agot_bg_website.Tests.Migration;

/// <summary>
/// Tests for the historical PreviousPlayerInGame backfill (MIGRATION_PLAN.md §10.1). The main
/// fixture mirrors the flat shape of a real production `view_of_game` JSON the user supplied to
/// design this against (top-level `oldPlayerIds`/`timeoutPlayerIds`, no `childGameState` nesting -
/// see EntireGame.getViewOfGame()). "Currently a player" is passed in explicitly (as it would be
/// from the legacy PlayerInGame table), since ViewOfGame itself has no raw current-player user-id
/// list.
///
/// Deliberately does not resolve House, winner, or a precise removal timestamp - see
/// PreviousPlayersBackfill's class doc comment for why.
/// </summary>
public class PreviousPlayersBackfillTests
{
    // Mirrors a real view_of_game: two players were voted out (luffy/1c1dea0e, ranger/99838f66),
    // one vote to replace a third player failed (b4fddf3d is still a current player, so must NOT
    // produce a row).
    private const string RealGameFixture = """
        {
            "turn": 10,
            "oldPlayerIds": [
                "1c1dea0e-96c1-48c3-b22d-61fbd935e8ac",
                "99838f66-13de-452b-ac88-9bf34085439f"
            ]
        }
        """;

    private static readonly Guid StillPresentUserId = Guid.Parse(
        "b4fddf3d-deaf-4380-9af3-00513744ec95"
    );

    [Fact]
    public void ComputeReturnsARowPerVotedOutPlayerNotCurrentlyInTheGame()
    {
        var gameId = Guid.NewGuid();
        using var doc = JsonDocument.Parse(RealGameFixture);

        var result = PreviousPlayersBackfill.Compute(gameId, doc, [StillPresentUserId]);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal(gameId, r.GameId));
        Assert.All(result, r => Assert.Equal(PlayerReplacementReason.Vote, r.Reason));
        Assert.All(result, r => Assert.Null(r.ReplacedAt));
        Assert.Contains(
            result,
            r => r.UserId == Guid.Parse("1c1dea0e-96c1-48c3-b22d-61fbd935e8ac")
        );
        Assert.Contains(
            result,
            r => r.UserId == Guid.Parse("99838f66-13de-452b-ac88-9bf34085439f")
        );
        // b4fddf3d's replacement vote failed and it's still a current player - must not appear at all.
        Assert.DoesNotContain(result, r => r.UserId == StillPresentUserId);
    }

    [Fact]
    public void ComputeResolvesVoteReasonFromOldPlayerIds()
    {
        var userId = Guid.Parse("1c1dea0e-96c1-48c3-b22d-61fbd935e8ac");
        using var doc = JsonDocument.Parse(RealGameFixture);

        var result = PreviousPlayersBackfill.Compute(Guid.NewGuid(), doc, [StillPresentUserId]);

        Assert.Contains(
            result,
            r => r.UserId == userId && r.Reason == PlayerReplacementReason.Vote
        );
    }

    [Fact]
    public void ComputeSkipsAPlayerWhoWasVotedOutButLaterVotedBackIn()
    {
        var userId = Guid.Parse("1c1dea0e-96c1-48c3-b22d-61fbd935e8ac");
        var json = $$"""
            {
                "oldPlayerIds": ["{{userId}}"]
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var result = PreviousPlayersBackfill.Compute(Guid.NewGuid(), doc, [userId]);

        Assert.Empty(result);
    }

    [Fact]
    public void ComputeHandlesTimeoutRemovals()
    {
        var timedOutUserId = Guid.NewGuid();
        var stillPresentUserId = Guid.NewGuid();
        var json = $$"""
            {
                "timeoutPlayerIds": ["{{timedOutUserId}}"]
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var result = PreviousPlayersBackfill.Compute(Guid.NewGuid(), doc, [stillPresentUserId]);

        var row = Assert.Single(result);
        Assert.Equal(timedOutUserId, row.UserId);
        Assert.Equal(PlayerReplacementReason.ClockTimeout, row.Reason);
        Assert.Null(row.ReplacedAt);
    }

    [Fact]
    public void ComputeDoesNotDuplicateAUserPresentInBothOldAndTimeoutArrays()
    {
        // Defensive guard only - oldPlayerIds/timeoutPlayerIds are disjoint by construction, but
        // Compute must not produce two rows for the same user regardless.
        var userId = Guid.NewGuid();
        var json = $$"""
            {
                "oldPlayerIds": ["{{userId}}"],
                "timeoutPlayerIds": ["{{userId}}"]
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var result = PreviousPlayersBackfill.Compute(Guid.NewGuid(), doc, []);

        var row = Assert.Single(result);
        Assert.Equal(userId, row.UserId);
    }

    [Fact]
    public void ComputeReturnsEmptyWhenNeitherArrayIsPresent()
    {
        using var doc = JsonDocument.Parse("""{ "turn": 3 }""");

        Assert.Empty(PreviousPlayersBackfill.Compute(Guid.NewGuid(), doc, []));
    }

    [Fact]
    public void ComputeReturnsEmptyForNullViewOfGame() =>
        Assert.Empty(PreviousPlayersBackfill.Compute(Guid.NewGuid(), null, []));
}
