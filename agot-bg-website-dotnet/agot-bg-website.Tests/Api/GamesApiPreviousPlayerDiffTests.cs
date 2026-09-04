using agot_bg_website.Api;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace agot_bg_website.Tests.Api;

/// <summary>
/// Tests for <see cref="GamesApi.DiffPreviousPlayers"/> and the PATCH handler's use of it to
/// compute <see cref="PreviousPlayerInGame"/> rows itself — the game server never sends this data
/// (see GamesApi.cs's class doc comment), so it must be derived purely from diffing the Players
/// list across saves.
/// </summary>
public class GamesApiPreviousPlayerDiffTests
{
    [Fact]
    public void PlayerMissingFromNewListAndNotAlreadyTrackedIsAdded()
    {
        var stillHere = Guid.NewGuid();
        var removed = Guid.NewGuid();

        var (toAdd, toRemove) = GamesApi.DiffPreviousPlayers(
            oldPlayerUserIds: [stillHere, removed],
            newPlayerUserIds: [stillHere],
            existingPreviousPlayerUserIds: []
        );

        Assert.Equal([removed], toAdd);
        Assert.Empty(toRemove);
    }

    [Fact]
    public void PlayerBackInTheNewListWhoHadAnExistingRowIsRemoved()
    {
        var votedBackIn = Guid.NewGuid();

        var (toAdd, toRemove) = GamesApi.DiffPreviousPlayers(
            oldPlayerUserIds: [],
            newPlayerUserIds: [votedBackIn],
            existingPreviousPlayerUserIds: [votedBackIn]
        );

        Assert.Empty(toAdd);
        Assert.Equal([votedBackIn], toRemove);
    }

    [Fact]
    public void PlayerRemovedWhoAlreadyHasARowIsNotAddedAgain()
    {
        var alreadyTracked = Guid.NewGuid();

        var (toAdd, toRemove) = GamesApi.DiffPreviousPlayers(
            oldPlayerUserIds: [alreadyTracked],
            newPlayerUserIds: [],
            existingPreviousPlayerUserIds: [alreadyTracked]
        );

        Assert.Empty(toAdd);
        Assert.Empty(toRemove);
    }

    [Fact]
    public void UnrelatedPlayersProduceNoChanges()
    {
        var stillHere = Guid.NewGuid();

        var (toAdd, toRemove) = GamesApi.DiffPreviousPlayers(
            oldPlayerUserIds: [stillHere],
            newPlayerUserIds: [stillHere],
            existingPreviousPlayerUserIds: []
        );

        Assert.Empty(toAdd);
        Assert.Empty(toRemove);
    }

    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// End-to-end style test replicating the exact sequence GamesApi.cs's PATCH handler performs:
    /// a player is removed on one save (recorded as a PreviousPlayerInGame row), then reappears on
    /// a later save (the row is removed again), matching a vote-out followed by a vote-back-in.
    /// </summary>
    [Fact]
    public async Task RemovingThenReAddingAPlayerAddsThenRemovesThePreviousPlayerRow()
    {
        await using var db = CreateContext(
            nameof(RemovingThenReAddingAPlayerAddsThenRemovesThePreviousPlayerRow)
        );

        var gameId = Guid.NewGuid();
        var stayingUserId = Guid.NewGuid();
        var votedOutUserId = Guid.NewGuid();

        db.Games.Add(
            new Game
            {
                Id = gameId,
                Name = "Test Game",
                OwnerUserId = Guid.NewGuid(),
            }
        );
        db.PlayersInGame.AddRange(
            new PlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = gameId,
                UserId = stayingUserId,
                Data = System.Text.Json.JsonDocument.Parse("{}"),
            },
            new PlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = gameId,
                UserId = votedOutUserId,
                Data = System.Text.Json.JsonDocument.Parse("{}"),
            }
        );
        await db.SaveChangesAsync();

        // --- Save #1: votedOutUserId is missing from the new player list. ---
        await using (
            var db1 = CreateContext(
                nameof(RemovingThenReAddingAPlayerAddsThenRemovesThePreviousPlayerRow)
            )
        )
        {
            var game = await db1
                .Games.Include(g => g.Players)
                .Include(g => g.PreviousPlayers)
                .FirstAsync(g => g.Id == gameId);

            var newPlayerIds = new[] { stayingUserId };
            var (toAdd, toRemove) = GamesApi.DiffPreviousPlayers(
                oldPlayerUserIds: game.Players.Select(p => p.UserId),
                newPlayerUserIds: newPlayerIds,
                existingPreviousPlayerUserIds: game.PreviousPlayers.Select(p => p.UserId)
            );

            db1.PlayersInGame.RemoveRange(game.Players);
            var newPlayers = newPlayerIds
                .Select(uid => new PlayerInGame
                {
                    Id = Guid.NewGuid(),
                    GameId = gameId,
                    UserId = uid,
                    Data = System.Text.Json.JsonDocument.Parse("{}"),
                })
                .ToList();
            db1.PlayersInGame.AddRange(newPlayers);
            game.Players = newPlayers;

            db1.PreviousPlayersInGame.RemoveRange(
                game.PreviousPlayers.Where(p => toRemove.Contains(p.UserId))
            );
            // No ViewOfGame is set up on this fixture's Game, so PreviousPlayerReasonResolver
            // resolves Reason to null here (see GamesApi.cs's real PATCH handler, which passes
            // game.ViewOfGame - this test replicates the diff/persist steps directly rather than
            // invoking the minimal-API lambda itself, so it calls the resolver the same way).
            db1.PreviousPlayersInGame.AddRange(
                toAdd.Select(uid => new PreviousPlayerInGame
                {
                    Id = Guid.NewGuid(),
                    GameId = gameId,
                    UserId = uid,
                    Reason = PreviousPlayerReasonResolver.Resolve(game.ViewOfGame, uid),
                    ReplacedAt = DateTimeOffset.UtcNow,
                })
            );
            await db1.SaveChangesAsync();
        }

        await using (
            var check1 = CreateContext(
                nameof(RemovingThenReAddingAPlayerAddsThenRemovesThePreviousPlayerRow)
            )
        )
        {
            var rows = await check1
                .PreviousPlayersInGame.Where(p => p.GameId == gameId)
                .ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal(votedOutUserId, row.UserId);
            Assert.Null(row.Reason);
        }

        // --- Save #2: votedOutUserId is voted back in. ---
        await using (
            var db2 = CreateContext(
                nameof(RemovingThenReAddingAPlayerAddsThenRemovesThePreviousPlayerRow)
            )
        )
        {
            var game = await db2
                .Games.Include(g => g.Players)
                .Include(g => g.PreviousPlayers)
                .FirstAsync(g => g.Id == gameId);

            var newPlayerIds = new[] { stayingUserId, votedOutUserId };
            var (toAdd, toRemove) = GamesApi.DiffPreviousPlayers(
                oldPlayerUserIds: game.Players.Select(p => p.UserId),
                newPlayerUserIds: newPlayerIds,
                existingPreviousPlayerUserIds: game.PreviousPlayers.Select(p => p.UserId)
            );

            db2.PlayersInGame.RemoveRange(game.Players);
            var newPlayers = newPlayerIds
                .Select(uid => new PlayerInGame
                {
                    Id = Guid.NewGuid(),
                    GameId = gameId,
                    UserId = uid,
                    Data = System.Text.Json.JsonDocument.Parse("{}"),
                })
                .ToList();
            db2.PlayersInGame.AddRange(newPlayers);
            game.Players = newPlayers;

            db2.PreviousPlayersInGame.RemoveRange(
                game.PreviousPlayers.Where(p => toRemove.Contains(p.UserId))
            );
            db2.PreviousPlayersInGame.AddRange(
                toAdd.Select(uid => new PreviousPlayerInGame
                {
                    Id = Guid.NewGuid(),
                    GameId = gameId,
                    UserId = uid,
                    Reason = PreviousPlayerReasonResolver.Resolve(game.ViewOfGame, uid),
                    ReplacedAt = DateTimeOffset.UtcNow,
                })
            );
            await db2.SaveChangesAsync();
        }

        await using var check2 = CreateContext(
            nameof(RemovingThenReAddingAPlayerAddsThenRemovesThePreviousPlayerRow)
        );
        Assert.Empty(
            await check2.PreviousPlayersInGame.Where(p => p.GameId == gameId).ToListAsync()
        );
    }
}
