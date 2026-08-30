using System.Text.Json;
using agot_bg_website.Domain;
using Xunit;

namespace agot_bg_website.Tests.Domain;

/// <summary>
/// Tests for <see cref="PreviousPlayerReasonResolver"/>, shared by GamesApi.cs's live PATCH
/// handler and Snr.Migration's historical backfill (PreviousPlayersBackfill.cs).
/// </summary>
public class PreviousPlayerReasonResolverTests
{
    [Fact]
    public void ResolveReturnsVoteWhenUserIsInOldPlayerIds()
    {
        var userId = Guid.NewGuid();
        using var doc = JsonDocument.Parse($$"""{ "oldPlayerIds": ["{{userId}}"] }""");

        Assert.Equal(
            PlayerReplacementReason.Vote,
            PreviousPlayerReasonResolver.Resolve(doc, userId)
        );
    }

    [Fact]
    public void ResolveReturnsClockTimeoutWhenUserIsInTimeoutPlayerIds()
    {
        var userId = Guid.NewGuid();
        using var doc = JsonDocument.Parse($$"""{ "timeoutPlayerIds": ["{{userId}}"] }""");

        Assert.Equal(
            PlayerReplacementReason.ClockTimeout,
            PreviousPlayerReasonResolver.Resolve(doc, userId)
        );
    }

    [Fact]
    public void ResolveReturnsNullWhenUserIsInNeitherArray()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        using var doc = JsonDocument.Parse($$"""{ "oldPlayerIds": ["{{otherUserId}}"] }""");

        Assert.Null(PreviousPlayerReasonResolver.Resolve(doc, userId));
    }

    [Fact]
    public void ResolveReturnsNullWhenViewOfGameIsNull() =>
        Assert.Null(PreviousPlayerReasonResolver.Resolve(null, Guid.NewGuid()));

    [Fact]
    public void ResolveIsCaseInsensitiveOnTheGuidString()
    {
        var userId = Guid.NewGuid();
        using var doc = JsonDocument.Parse(
            $$"""{ "oldPlayerIds": ["{{userId.ToString().ToUpperInvariant()}}"] }"""
        );

        Assert.Equal(
            PlayerReplacementReason.Vote,
            PreviousPlayerReasonResolver.Resolve(doc, userId)
        );
    }
}
