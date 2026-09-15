using agot_bg_website.Api;
using agot_bg_website.Domain;
using Xunit;

namespace agot_bg_website.Tests.Api;

/// <summary>
/// Tests for <see cref="GamesApi.JustFinishedOrCancelled"/>: the check the PATCH handler uses to
/// decide whether a save's state transition must enqueue a cached win-rate stats recalculation
/// for every current and former participant. Both Finished and Cancelled transitions matter -
/// see the class-level doc comment on <see cref="GamesApi"/> and <see cref="GamesApi.JustFinishedOrCancelled"/>'s
/// own doc comment for why a still-Ongoing game later being cancelled must refresh stats too, not
/// just a game reaching Finished.
/// </summary>
public class GamesApiJustFinishedOrCancelledTests
{
    [Fact]
    public void OngoingToFinishedIsTrue() =>
        Assert.True(GamesApi.JustFinishedOrCancelled(GameState.Ongoing, GameState.Finished));

    [Fact]
    public void OngoingToCancelledIsTrue() =>
        Assert.True(GamesApi.JustFinishedOrCancelled(GameState.Ongoing, GameState.Cancelled));

    [Fact]
    public void InLobbyToCancelledIsTrue() =>
        Assert.True(GamesApi.JustFinishedOrCancelled(GameState.InLobby, GameState.Cancelled));

    [Fact]
    public void StayingFinishedIsFalse() =>
        Assert.False(GamesApi.JustFinishedOrCancelled(GameState.Finished, GameState.Finished));

    [Fact]
    public void StayingCancelledIsFalse() =>
        Assert.False(GamesApi.JustFinishedOrCancelled(GameState.Cancelled, GameState.Cancelled));

    [Fact]
    public void StayingOngoingIsFalse() =>
        Assert.False(GamesApi.JustFinishedOrCancelled(GameState.Ongoing, GameState.Ongoing));

    [Fact]
    public void InLobbyToOngoingIsFalse() =>
        Assert.False(GamesApi.JustFinishedOrCancelled(GameState.InLobby, GameState.Ongoing));
}
