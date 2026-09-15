using agot_bg_website.Api;
using Xunit;

namespace agot_bg_website.Tests.Api;

/// <summary>
/// Tests for <see cref="GamesApi.IsStaleSave"/>: the guard the PATCH handler uses to reject a save
/// whose <see cref="Domain.Game.SaveSequence"/> isn't strictly greater than what's already stored,
/// which is what makes saves safe against the game server's fire-and-forget PATCHes completing out
/// of order (see <see cref="Domain.Game.SaveSequence"/>'s doc comment for the full reasoning).
/// </summary>
public class GamesApiSaveSequenceTests
{
    [Fact]
    public void HigherIncomingSequenceIsNotStale()
    {
        Assert.False(GamesApi.IsStaleSave(incomingSaveSequence: 5, storedSaveSequence: 4));
    }

    [Fact]
    public void EqualIncomingSequenceIsStale()
    {
        // A duplicate/replay of a save that already applied must not re-apply.
        Assert.True(GamesApi.IsStaleSave(incomingSaveSequence: 4, storedSaveSequence: 4));
    }

    [Fact]
    public void LowerIncomingSequenceIsStale()
    {
        // The out-of-order case this guard exists for: an older save arriving after a newer one.
        Assert.True(GamesApi.IsStaleSave(incomingSaveSequence: 3, storedSaveSequence: 4));
    }

    [Fact]
    public void MissingIncomingSequenceIsNeverStale()
    {
        // A patch from a game-server build that predates this field (e.g. momentarily during a
        // rolling deploy) must not be rejected - the guard quietly no-ops instead.
        Assert.False(GamesApi.IsStaleSave(incomingSaveSequence: null, storedSaveSequence: 100));
    }

    [Fact]
    public void FirstEverSaveWithSequenceOneIsNotStaleAgainstDefaultZero()
    {
        Assert.False(GamesApi.IsStaleSave(incomingSaveSequence: 1, storedSaveSequence: 0));
    }
}
