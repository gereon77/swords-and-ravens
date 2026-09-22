using System.Text.Json;
using agot_bg_website.Api;
using agot_bg_website.Domain;
using Xunit;

namespace agot_bg_website.Tests.Api;

/// <summary>
/// Tests for <see cref="GamesApi.PlayersUnchanged"/>: the check the PATCH handler uses to skip the
/// Players/PreviousPlayers delete+recreate entirely when a save's incoming player list is
/// identical to what's already stored, avoiding pointless churn on saves that only change
/// SerializedGame/ViewOfGame (the vast majority of saves during a live game).
/// </summary>
public class GamesApiPlayersUnchangedTests
{
    private static PlayerInGame MakePlayer(Guid userId, string data = "{}") =>
        new()
        {
            Id = Guid.NewGuid(),
            GameId = Guid.NewGuid(),
            UserId = userId,
            Data = JsonDocument.Parse(data),
        };

    private static PlayerInGamePatchDto MakePatch(Guid userId, string data = "{}") =>
        new(userId, JsonDocument.Parse(data).RootElement);

    [Fact]
    public void IdenticalSinglePlayerListIsUnchanged()
    {
        var userId = Guid.NewGuid();

        Assert.True(
            GamesApi.PlayersUnchanged(
                [MakePlayer(userId, """{"house":"Stark"}""")],
                [MakePatch(userId, """{"house":"Stark"}""")]
            )
        );
    }

    // Pinned repro for the real-world bug this method must not regress to: PlayerInGame.Data is a
    // jsonb column, and Postgres reorders object keys (and reformats whitespace) whenever it reads
    // a jsonb value back out - it never preserves the original text. A save whose Data is
    // semantically identical to what's stored, just with differently-ordered/-formatted
    // properties (exactly what a fresh EF Core read of a jsonb column looks like versus the game
    // server's freshly-serialized patch), must still compare as unchanged.
    [Fact]
    public void SameDataWithDifferentPropertyOrderAndWhitespaceIsUnchanged()
    {
        var userId = Guid.NewGuid();

        Assert.True(
            GamesApi.PlayersUnchanged(
                [
                    MakePlayer(
                        userId,
                        """{"house": "stark", "is_winner": false, "waited_for": true, "needed_for_vote": false, "important_chat_rooms": []}"""
                    ),
                ],
                [
                    MakePatch(
                        userId,
                        """{"house":"stark","waited_for":true,"important_chat_rooms":[],"is_winner":false,"needed_for_vote":false}"""
                    ),
                ]
            )
        );
    }

    [Fact]
    public void DifferentDataForSameUserIsChanged()
    {
        var userId = Guid.NewGuid();

        Assert.False(
            GamesApi.PlayersUnchanged(
                [MakePlayer(userId, """{"house":"Stark"}""")],
                [MakePatch(userId, """{"house":"Lannister"}""")]
            )
        );
    }

    [Fact]
    public void DifferentPlayerCountIsChanged()
    {
        var userId = Guid.NewGuid();

        Assert.False(
            GamesApi.PlayersUnchanged(
                [MakePlayer(userId)],
                [MakePatch(userId), MakePatch(Guid.NewGuid())]
            )
        );
    }

    [Fact]
    public void SameCountButDifferentUserIsChanged()
    {
        Assert.False(
            GamesApi.PlayersUnchanged([MakePlayer(Guid.NewGuid())], [MakePatch(Guid.NewGuid())])
        );
    }

    [Fact]
    public void EmptyListsAreUnchanged()
    {
        Assert.True(GamesApi.PlayersUnchanged([], []));
    }
}
