using System.Linq;
using System.Text.Json;
using agot_bg_website.Domain;
using Snr.Migration;
using Xunit;

namespace agot_bg_website.Tests.Migration;

/// <summary>
/// Tests for the historical PreviousPlayerInGame backfill (MIGRATION_PLAN.md §10.1). The main
/// fixture is a trimmed-down version of a real production game JSON the user supplied to design
/// this against, so the shape (in particular `oldPlayerIds`/`childGameState`) is authentic.
///
/// Deliberately does not resolve House, winner, or a precise removal timestamp - see
/// PreviousPlayersBackfill's class doc comment for why.
/// </summary>
public class PreviousPlayersBackfillTests
{
    // Trimmed from a real serialized game: two players were voted out (luffy/1c1dea0e,
    // ranger/99838f66), one vote to replace a third player failed (b4fddf3d is still present in
    // `players`, so must NOT produce a row).
    private const string RealGameFixture = """
        {
            "childGameState": {
                "type": "ingame",
                "players": [
                    { "userId": "60d6ca85-8ebd-4e33-9b51-61b8a662d21b", "houseId": "martell" },
                    { "userId": "d1590694-f7e3-474e-b1a3-4439178eddf4", "houseId": "baratheon" },
                    { "userId": "b4fddf3d-deaf-4380-9af3-00513744ec95", "houseId": "lannister" },
                    { "userId": "dc0b3d43-2894-49d5-b4d9-340d818d5f5a", "houseId": "greyjoy" },
                    { "userId": "df0a76e7-cc87-48f2-860d-a3a13bf80a26", "houseId": "stark" }
                ],
                "oldPlayerIds": [
                    "1c1dea0e-96c1-48c3-b22d-61fbd935e8ac",
                    "99838f66-13de-452b-ac88-9bf34085439f"
                ],
                "childGameState": { "type": "game-ended", "winner": "stark" }
            }
        }
        """;

    [Fact]
    public void ComputeReturnsARowPerVotedOutPlayerNotCurrentlyInTheGame()
    {
        var gameId = Guid.NewGuid();
        using var doc = JsonDocument.Parse(RealGameFixture);

        var result = PreviousPlayersBackfill.Compute(gameId, doc);

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
        // b4fddf3d's replacement vote failed and it's still in `players` - must not appear at all.
        Assert.DoesNotContain(
            result,
            r => r.UserId == Guid.Parse("b4fddf3d-deaf-4380-9af3-00513744ec95")
        );
    }

    [Fact]
    public void ComputeSkipsAPlayerWhoWasVotedOutButLaterVotedBackIn()
    {
        var userId = "1c1dea0e-96c1-48c3-b22d-61fbd935e8ac";
        var json = $$"""
            {
                "childGameState": {
                    "type": "ingame",
                    "players": [
                        { "userId": "{{userId}}", "houseId": "tyrell" }
                    ],
                    "oldPlayerIds": ["{{userId}}"]
                }
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var result = PreviousPlayersBackfill.Compute(Guid.NewGuid(), doc);

        Assert.Empty(result);
    }

    [Fact]
    public void ComputeHandlesTimeoutRemovals()
    {
        var timedOutUserId = Guid.NewGuid();
        var stillPresentUserId = Guid.NewGuid();
        var json = $$"""
            {
                "childGameState": {
                    "type": "ingame",
                    "players": [
                        { "userId": "{{stillPresentUserId}}", "houseId": "stark" }
                    ],
                    "timeoutPlayerIds": ["{{timedOutUserId}}"]
                }
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var result = PreviousPlayersBackfill.Compute(Guid.NewGuid(), doc);

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
                "childGameState": {
                    "type": "ingame",
                    "players": [],
                    "oldPlayerIds": ["{{userId}}"],
                    "timeoutPlayerIds": ["{{userId}}"]
                }
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var result = PreviousPlayersBackfill.Compute(Guid.NewGuid(), doc);

        var row = Assert.Single(result);
        Assert.Equal(userId, row.UserId);
    }

    [Fact]
    public void ComputeReturnsEmptyWhenGameNeverReachedIngameState()
    {
        using var doc = JsonDocument.Parse("""{ "childGameState": { "type": "lobby" } }""");

        Assert.Empty(PreviousPlayersBackfill.Compute(Guid.NewGuid(), doc));
    }

    [Fact]
    public void ComputeReturnsEmptyForNullSerializedGame() =>
        Assert.Empty(PreviousPlayersBackfill.Compute(Guid.NewGuid(), null));
}
