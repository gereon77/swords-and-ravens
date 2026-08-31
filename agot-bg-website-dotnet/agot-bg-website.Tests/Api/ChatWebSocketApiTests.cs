using agot_bg_website.Api;
using Xunit;

namespace agot_bg_website.Tests.Api;

/// <summary>
/// Unit tests for the pure logic extracted from <see cref="ChatWebSocketApi"/> (MIGRATION_PLAN.md
/// §7) — the tongueless-message regex and the retrieve-count capping rule. The WebSocket handler
/// itself needs a live DbContext/socket/Redis, so it's exercised via LOCAL_DEV_VERIFICATION.md's
/// manual end-to-end steps instead of here.
/// </summary>
public class ChatWebSocketApiTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("9")]
    [InlineData("+")]
    [InlineData("-")]
    public void TonguelessMessageRegex_AllowsSingleDigitOrPlusMinus(string text)
    {
        Assert.Matches(ChatWebSocketApi.TonguelessMessageRegex, text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("10")]
    [InlineData("hello")]
    [InlineData("++")]
    [InlineData(" ")]
    public void TonguelessMessageRegex_RejectsAnythingElse(string text)
    {
        Assert.DoesNotMatch(ChatWebSocketApi.TonguelessMessageRegex, text);
    }

    [Fact]
    public void ResolveRetrieveCount_ReturnsRequestedCount_WhenNoRoomCap()
    {
        Assert.Equal(30, ChatWebSocketApi.ResolveRetrieveCount(30, maxRetrieveCount: null));
    }

    [Fact]
    public void ResolveRetrieveCount_CapsAtRoomMax_WhenRequestExceedsIt()
    {
        Assert.Equal(50, ChatWebSocketApi.ResolveRetrieveCount(200, maxRetrieveCount: 50));
    }

    [Fact]
    public void ResolveRetrieveCount_KeepsRequestedCount_WhenBelowRoomMax()
    {
        Assert.Equal(10, ChatWebSocketApi.ResolveRetrieveCount(10, maxRetrieveCount: 50));
    }
}
