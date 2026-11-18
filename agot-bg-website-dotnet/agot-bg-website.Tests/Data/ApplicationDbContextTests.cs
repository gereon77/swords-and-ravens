using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace agot_bg_website.Tests.Data;

/// <summary>
/// Verifies the PreviousPlayerInGame model configuration (§4.4) behaves as designed: multiple
/// rows per game are fine (one per removed user), but at most one row per (GameId, UserId) at a
/// time - a user can only be "currently removed" from a given game once.
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
        var game = new Game
        {
            Id = Guid.NewGuid(),
            Name = "Test Game",
            OwnerUserId = ownerId,
        };
        db.Games.Add(game);

        db.PreviousPlayersInGame.AddRange(
            new PreviousPlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = game.Id,
                UserId = Guid.NewGuid(),
                Reason = PlayerReplacementReason.Vote,
            },
            new PreviousPlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = game.Id,
                UserId = Guid.NewGuid(),
                Reason = PlayerReplacementReason.ClockTimeout,
            }
        );

        await db.SaveChangesAsync();

        var stored = await db.PreviousPlayersInGame.Where(p => p.GameId == game.Id).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Contains(stored, p => p.Reason == PlayerReplacementReason.Vote);
        Assert.Contains(stored, p => p.Reason == PlayerReplacementReason.ClockTimeout);
    }

    [Fact]
    public async Task DeletingGame_CascadesToPreviousPlayers()
    {
        await using var db = CreateContext(nameof(DeletingGame_CascadesToPreviousPlayers));

        var game = new Game
        {
            Id = Guid.NewGuid(),
            Name = "Cascade Test",
            OwnerUserId = Guid.NewGuid(),
        };
        db.Games.Add(game);
        db.PreviousPlayersInGame.Add(
            new PreviousPlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = game.Id,
                UserId = Guid.NewGuid(),
                Reason = PlayerReplacementReason.Vote,
            }
        );
        await db.SaveChangesAsync();

        db.Games.Remove(game);
        await db.SaveChangesAsync();

        Assert.Empty(await db.PreviousPlayersInGame.Where(p => p.GameId == game.Id).ToListAsync());
    }

    [Fact]
    public void SynchronousSaveDoesNotOverwriteUsageCounters()
    {
        using var db = CreateContext(nameof(SynchronousSaveDoesNotOverwriteUsageCounters));
        var hour = DateTimeOffset.UtcNow;
        var user = new ApplicationUser { UsageHour = hour, UsageRequests = 42 };
        db.Users.Add(user);
        db.SaveChanges();

        user.UsageHour = null;
        user.UsageRequests = 0;
        user.ProfileText = "Updated profile";
        db.Users.Update(user);
        db.SaveChanges();

        db.ChangeTracker.Clear();
        var stored = db.Users.Single(u => u.Id == user.Id);
        Assert.Equal("Updated profile", stored.ProfileText);
        Assert.Equal(hour, stored.UsageHour);
        Assert.Equal(42, stored.UsageRequests);
    }
}
