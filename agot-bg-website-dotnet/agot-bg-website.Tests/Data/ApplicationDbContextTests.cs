using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace agot_bg_website.Tests.Data;

/// <summary>
/// Verifies the PreviousPlayerInGame model configuration (§4.4) behaves as designed: multiple
/// rows per game are fine, but SequenceNumber must be unique within a game.
/// </summary>
public class ApplicationDbContextTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CanStoreMultiplePreviousPlayersForOneGame()
    {
        await using var db = CreateContext(nameof(CanStoreMultiplePreviousPlayersForOneGame));

        var ownerId = Guid.NewGuid();
        var game = new Game { Id = Guid.NewGuid(), Name = "Test Game", OwnerUserId = ownerId };
        db.Games.Add(game);

        db.PreviousPlayersInGame.AddRange(
            new PreviousPlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = game.Id,
                UserId = Guid.NewGuid(),
                House = "stark",
                SequenceNumber = 0,
                Reason = PlayerReplacementReason.Vote
            },
            new PreviousPlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = game.Id,
                UserId = Guid.NewGuid(),
                House = "lannister",
                SequenceNumber = 1,
                Reason = PlayerReplacementReason.ClockTimeout,
                WasWinner = false
            });

        await db.SaveChangesAsync();

        var stored = await db.PreviousPlayersInGame.Where(p => p.GameId == game.Id).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Contains(stored, p => p.House == "stark" && p.Reason == PlayerReplacementReason.Vote);
        Assert.Contains(stored, p => p.House == "lannister" && p.WasWinner == false);
    }

    [Fact]
    public async Task DeletingGame_CascadesToPreviousPlayers()
    {
        await using var db = CreateContext(nameof(DeletingGame_CascadesToPreviousPlayers));

        var game = new Game { Id = Guid.NewGuid(), Name = "Cascade Test", OwnerUserId = Guid.NewGuid() };
        db.Games.Add(game);
        db.PreviousPlayersInGame.Add(new PreviousPlayerInGame
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            UserId = Guid.NewGuid(),
            House = "tyrell",
            SequenceNumber = 0,
            Reason = PlayerReplacementReason.ReplacedByPlayer
        });
        await db.SaveChangesAsync();

        db.Games.Remove(game);
        await db.SaveChangesAsync();

        Assert.Empty(await db.PreviousPlayersInGame.Where(p => p.GameId == game.Id).ToListAsync());
    }
}
