using agot_bg_website.Domain;
using Xunit;

namespace agot_bg_website.Tests.Domain;

/// <summary>
/// Tests for <see cref="PreviousPlayerCleanup.IsLikelyLobbyChurnArtifact"/>, the predicate that
/// identifies <see cref="PreviousPlayerInGame"/> rows corrupted by the now-fixed
/// lobby-seat-change bug so they can be reviewed/deleted via the admin cleanup page.
/// </summary>
public class PreviousPlayerCleanupTests
{
    [Fact]
    public void FlagsALiveCreatedRowWithNoResolvedReason()
    {
        var row = MakeRow(reason: null, replacedAt: DateTimeOffset.UtcNow);

        Assert.True(Compile(row));
    }

    [Fact]
    public void DoesNotFlagALegacyBackfilledRowWithNoResolvedReason()
    {
        // Snr.Migration's PreviousPlayersBackfill always leaves ReplacedAt null - this is a
        // legitimate pre-feature legacy removal, not the lobby bug.
        var row = MakeRow(reason: null, replacedAt: null);

        Assert.False(Compile(row));
    }

    [Fact]
    public void DoesNotFlagALiveCreatedRowWithAResolvedVoteReason()
    {
        var row = MakeRow(reason: PlayerReplacementReason.Vote, replacedAt: DateTimeOffset.UtcNow);

        Assert.False(Compile(row));
    }

    [Fact]
    public void DoesNotFlagALiveCreatedRowWithAResolvedClockTimeoutReason()
    {
        var row = MakeRow(
            reason: PlayerReplacementReason.ClockTimeout,
            replacedAt: DateTimeOffset.UtcNow
        );

        Assert.False(Compile(row));
    }

    private static PreviousPlayerInGame MakeRow(
        PlayerReplacementReason? reason,
        DateTimeOffset? replacedAt
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            GameId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Reason = reason,
            ReplacedAt = replacedAt,
        };

    private static bool Compile(PreviousPlayerInGame row) =>
        PreviousPlayerCleanup.IsLikelyLobbyChurnArtifact.Compile()(row);
}
