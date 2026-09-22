using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace agot_bg_website.Tests.Api;

/// <summary>
/// Pins down the real cause of the <see cref="DbUpdateConcurrencyException"/>
/// ("expected to affect 1 row(s), but actually affected 0 row(s)") reported when the game server
/// saved a game right after a player took a seat.
///
/// It first looked like a race between overlapping saves (the game server's fire-and-forget
/// <c>saveGame()</c> can trigger multiple concurrent PATCH requests for the same game), but a live
/// repro showed the exception on a single, entirely sequential PATCH too. The actual bug: <c>Api/
/// GamesApi.cs</c>'s PATCH handler used to build the replacement <see cref="PlayerInGame"/>/<see
/// cref="PreviousPlayerInGame"/> rows and assign them straight to the tracked <see cref="Game"/>'s
/// navigation collection (<c>game.Players = newList</c>) without ever calling
/// <c>db.PlayersInGame.AddRange(...)</c>. Because each new row's <c>Id</c> is a client-set
/// (non-default) <see cref="Guid"/>, EF Core's automatic graph fixup assumed the row already
/// existed and generated an UPDATE instead of an INSERT — which of course affects 0 rows for a
/// row that was never actually inserted, and EF reports that as a concurrency exception. This
/// reproduces every time a player row is added, not just under concurrent requests.
///
/// <see cref="agot_bg_website.Infrastructure.GameSaveLock"/> (see its own tests) is still useful
/// defense-in-depth against genuinely overlapping saves racing the delete-then-recreate replace,
/// but it does not address this bug — the fix is the explicit <c>AddRange</c> calls in
/// <c>GamesApi.cs</c>, which these tests pin down directly.
/// </summary>
public class GamesApiPlayerReplacementTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task ReplacingPlayersWithoutExplicitlyAddingThemThrowsConcurrencyException()
    {
        // This test documents the *original* bug: assigning newly-created rows to the navigation
        // collection alone is not enough for EF Core to treat them as inserts.
        await using var db = CreateContext(
            nameof(ReplacingPlayersWithoutExplicitlyAddingThemThrowsConcurrencyException)
        );

        var game = new Game
        {
            Id = Guid.NewGuid(),
            Name = "Test Game",
            OwnerUserId = Guid.NewGuid(),
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();

        await using var db2 = CreateContext(
            nameof(ReplacingPlayersWithoutExplicitlyAddingThemThrowsConcurrencyException)
        );
        var loaded = await db2.Games.Include(g => g.Players).FirstAsync(g => g.Id == game.Id);

        db2.PlayersInGame.RemoveRange(loaded.Players);
        loaded.Players = new List<PlayerInGame>
        {
            new()
            {
                Id = Guid.NewGuid(),
                GameId = loaded.Id,
                UserId = Guid.NewGuid(),
                Data = MakeData(),
            },
        };
        // Deliberately NOT calling db2.PlayersInGame.AddRange(...) here, matching the original bug.

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task ReplacingPlayersWithExplicitAddRangeSucceeds()
    {
        await using var db = CreateContext(nameof(ReplacingPlayersWithExplicitAddRangeSucceeds));

        var game = new Game
        {
            Id = Guid.NewGuid(),
            Name = "Test Game",
            OwnerUserId = Guid.NewGuid(),
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();

        await using var db2 = CreateContext(nameof(ReplacingPlayersWithExplicitAddRangeSucceeds));
        var loaded = await db2.Games.Include(g => g.Players).FirstAsync(g => g.Id == game.Id);

        db2.PlayersInGame.RemoveRange(loaded.Players);
        var newPlayers = new List<PlayerInGame>
        {
            new()
            {
                Id = Guid.NewGuid(),
                GameId = loaded.Id,
                UserId = Guid.NewGuid(),
                Data = MakeData(),
            },
        };
        db2.PlayersInGame.AddRange(newPlayers);
        loaded.Players = newPlayers;

        await db2.SaveChangesAsync();

        await using var db3 = CreateContext(nameof(ReplacingPlayersWithExplicitAddRangeSucceeds));
        var reloaded = await db3.Games.Include(g => g.Players).FirstAsync(g => g.Id == game.Id);
        Assert.Single(reloaded.Players);
    }

    private static System.Text.Json.JsonDocument MakeData() =>
        System.Text.Json.JsonDocument.Parse("{}");
}
