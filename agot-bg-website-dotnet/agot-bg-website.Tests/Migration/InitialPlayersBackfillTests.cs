using System.Text.Json;
using Snr.Migration;
using Xunit;

namespace agot_bg_website.Tests.Migration;

/// <summary>
/// Tests for the historical `initialPlayerIds` backfill (MIGRATION_PLAN.md §14). Fixtures are
/// deliberately minimal slices of a real `SerializedGame` - only the handful of nested properties
/// <see cref="InitialPlayersBackfill.TryComputeInitialPlayerIds"/> actually reads
/// (`childGameState.type`/`gameLogManager.logs[].data`), mirroring
/// serializedGameMigrations.ts version "137" exactly.
/// </summary>
public class InitialPlayersBackfillTests
{
    private const string UserA = "1c1dea0e-96c1-48c3-b22d-61fbd935e8ac";
    private const string UserB = "99838f66-13de-452b-ac88-9bf34085439f";

    [Fact]
    public void ReturnsInitialPlayerIdsFromTheUserHouseAssignmentsLogEntry()
    {
        var json = $$"""
            {
                "childGameState": {
                    "type": "ingame",
                    "gameLogManager": {
                        "logs": [
                            {
                                "data": {
                                    "type": "some-other-log-entry"
                                }
                            },
                            {
                                "data": {
                                    "type": "user-house-assignments",
                                    "assignments": [
                                        ["stark", "{{UserA}}"],
                                        ["lannister", "{{UserB}}"]
                                    ]
                                }
                            }
                        ]
                    }
                }
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var found = InitialPlayersBackfill.TryComputeInitialPlayerIds(
            doc,
            out var initialPlayerIds
        );

        Assert.True(found);
        Assert.Equal(2, initialPlayerIds.Count);
        Assert.Contains(Guid.Parse(UserA), initialPlayerIds);
        Assert.Contains(Guid.Parse(UserB), initialPlayerIds);
    }

    [Fact]
    public void ReturnsFalseWhenTheGameNeverReachedIngame()
    {
        // e.g. cancelled straight out of the lobby - beginGame() (and its one-time
        // "user-house-assignments" log entry) never ran.
        using var doc = JsonDocument.Parse("""{ "childGameState": { "type": "lobby" } }""");

        var found = InitialPlayersBackfill.TryComputeInitialPlayerIds(
            doc,
            out var initialPlayerIds
        );

        Assert.False(found);
        Assert.Empty(initialPlayerIds);
    }

    [Fact]
    public void ReturnsFalseWhenIngameButNoAssignmentsLogEntryExists()
    {
        // Shouldn't normally happen for a real game (beginGame always logs it), but must fail
        // safe rather than pretend "nobody was an original player".
        using var doc = JsonDocument.Parse(
            """{ "childGameState": { "type": "ingame", "gameLogManager": { "logs": [] } } } """
        );

        var found = InitialPlayersBackfill.TryComputeInitialPlayerIds(
            doc,
            out var initialPlayerIds
        );

        Assert.False(found);
        Assert.Empty(initialPlayerIds);
    }

    [Fact]
    public void FindsTheAssignmentsLogEntryEvenWhenCancelledMidGame()
    {
        // CancelledGameState nests as ingame's own child (see IngameGameState.ts/VoteType.ts) -
        // the top-level childGameState.type stays "ingame", so no separate "cancelled" case is
        // needed in TryComputeInitialPlayerIds.
        var json = $$"""
            {
                "childGameState": {
                    "type": "ingame",
                    "childGameState": { "type": "cancelled" },
                    "gameLogManager": {
                        "logs": [
                            {
                                "data": {
                                    "type": "user-house-assignments",
                                    "assignments": [["stark", "{{UserA}}"]]
                                }
                            }
                        ]
                    }
                }
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var found = InitialPlayersBackfill.TryComputeInitialPlayerIds(
            doc,
            out var initialPlayerIds
        );

        Assert.True(found);
        Assert.Equal([Guid.Parse(UserA)], initialPlayerIds);
    }
}
