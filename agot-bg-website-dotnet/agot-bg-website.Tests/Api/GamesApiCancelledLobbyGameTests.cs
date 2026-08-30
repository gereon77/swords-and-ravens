using System.Text.Json;
using agot_bg_website.Api;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace agot_bg_website.Tests.Api;

/// <summary>
/// A game cancelled before it ever left the lobby (view_of_game.turn still -1) is deleted
/// outright by GamesApi's PATCH handler, instead of being kept around as a dead row forever -
/// including its public chat room and messages. These tests pin down the two small JSON helpers
/// the handler uses to make that decision (<see cref="GamesApi.IsTurnMinusOne"/>/
/// <see cref="GamesApi.TryGetPublicChatRoomId"/>, made `internal` for exactly this purpose) and
/// the cascade-delete behavior itself (Game -> Players/PreviousPlayers, Room -> Messages), the
/// same way GamesApiPlayerReplacementTests pins down the Players replace behavior.
/// </summary>
public class GamesApiCancelledLobbyGameTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    [Theory]
    [InlineData("""{"turn": -1}""", true)]
    [InlineData("""{"turn": 0}""", false)]
    [InlineData("""{"turn": 5}""", false)]
    [InlineData("""{}""", false)]
    public void IsTurnMinusOneReadsTheTurnField(string json, bool expected)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expected, GamesApi.IsTurnMinusOne(doc));
    }

    [Fact]
    public void IsTurnMinusOneReturnsFalseForNullDocument() =>
        Assert.False(GamesApi.IsTurnMinusOne(null));

    [Fact]
    public void TryGetPublicChatRoomIdParsesAValidGuid()
    {
        var roomId = Guid.NewGuid();
        using var doc = JsonDocument.Parse($$"""{"publicChatRoomId": "{{roomId}}"}""");
        Assert.Equal(roomId, GamesApi.TryGetPublicChatRoomId(doc));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"publicChatRoomId": "not-a-guid"}""")]
    [InlineData("""{"publicChatRoomId": 123}""")]
    public void TryGetPublicChatRoomIdReturnsNullWhenMissingOrInvalid(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Null(GamesApi.TryGetPublicChatRoomId(doc));
    }

    [Fact]
    public async Task DeletingACancelledLobbyGameCascadesToPlayersAndItsPublicChatRoom()
    {
        var dbName = nameof(DeletingACancelledLobbyGameCascadesToPlayersAndItsPublicChatRoom);
        var gameId = Guid.NewGuid();
        var roomId = Guid.NewGuid();

        await using (var db = CreateContext(dbName))
        {
            var room = new Room
            {
                Id = roomId,
                Name = "public-chat",
                Public = true,
            };
            db.Rooms.Add(room);
            db.Messages.Add(
                new Message
                {
                    Id = Guid.NewGuid(),
                    RoomId = roomId,
                    UserId = Guid.NewGuid(),
                    Text = "hello",
                }
            );

            var game = new Game
            {
                Id = gameId,
                Name = "Cancelled before it started",
                OwnerUserId = Guid.NewGuid(),
                State = GameState.Cancelled,
                ViewOfGame = JsonDocument.Parse(
                    $$"""{"turn": -1, "publicChatRoomId": "{{roomId}}"}"""
                ),
            };
            db.Games.Add(game);
            db.PlayersInGame.Add(
                new PlayerInGame
                {
                    Id = Guid.NewGuid(),
                    GameId = gameId,
                    UserId = Guid.NewGuid(),
                    Data = JsonDocument.Parse("{}"),
                }
            );
            await db.SaveChangesAsync();
        }

        // Mirrors exactly what GamesApi.cs's PATCH handler does once it decides to delete: look up
        // the public chat room via the same two helpers, remove it, then remove the game.
        await using (var db = CreateContext(dbName))
        {
            var game = await db
                .Games.Include(g => g.Players)
                .Include(g => g.PreviousPlayers)
                .FirstAsync(g => g.Id == gameId);

            Assert.True(GamesApi.IsTurnMinusOne(game.ViewOfGame));
            var publicChatRoomId = GamesApi.TryGetPublicChatRoomId(game.ViewOfGame);
            Assert.Equal(roomId, publicChatRoomId);

            var room = await db
                .Rooms.Include(r => r.Messages)
                .FirstOrDefaultAsync(r => r.Id == publicChatRoomId);
            Assert.NotNull(room);
            db.Rooms.Remove(room!);
            db.Games.Remove(game);

            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(dbName))
        {
            Assert.False(await db.Games.AnyAsync(g => g.Id == gameId));
            Assert.False(await db.PlayersInGame.AnyAsync(p => p.GameId == gameId));
            Assert.False(await db.Rooms.AnyAsync(r => r.Id == roomId));
            Assert.False(await db.Messages.AnyAsync(m => m.RoomId == roomId));
        }
    }
}
