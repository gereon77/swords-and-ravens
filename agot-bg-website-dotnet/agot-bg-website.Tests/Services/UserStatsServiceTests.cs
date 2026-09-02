using System.Text.Json;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace agot_bg_website.Tests.Services;

/// <summary>
/// Pins the exact stats rules requested for the profile page (see Pages.UserModel and
/// MIGRATION_PLAN.md §10.2): cancelled games never affect any cached stat at all, "finished
/// games" only counts games actually played to the end (never games left early), and games left
/// early only fold into the win-rate percentage's denominator, always as a loss.
/// </summary>
public class UserStatsServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UserStatsService _sut;

    public UserStatsServiceTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString())
        );
        services.AddLogging();
        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        _provider = services.BuildServiceProvider();
        _db = _provider.GetRequiredService<ApplicationDbContext>();
        _userManager = _provider.GetRequiredService<UserManager<ApplicationUser>>();
        _sut = new UserStatsService(_db);
    }

    private static JsonDocument Json(string json) => JsonDocument.Parse(json);

    [Fact]
    public async Task CancelledGames_NeverAffectAnyStat()
    {
        var user = new ApplicationUser { UserName = "cancelled_guy", Email = "c@example.com" };
        await _userManager.CreateAsync(user);

        // Still-current participation in a cancelled game - must not count as finished, won, or
        // lost, and must not appear in the win-rate denominator either.
        var cancelledGame = new Game
        {
            Id = Guid.NewGuid(),
            Name = "Cancelled mid-game",
            OwnerUserId = user.Id,
            State = GameState.Cancelled,
            ViewOfGame = Json("""{"settings": {"setupId": "base-game"}}"""),
        };
        _db.Games.Add(cancelledGame);
        _db.PlayersInGame.Add(
            new PlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = cancelledGame.Id,
                UserId = user.Id,
                Data = Json("""{"house": "stark", "is_winner": false}"""),
            }
        );

        // Removed early from a game that was later cancelled (rather than finished) - must not
        // count towards RemovedFromGameCount either, per "cancelled games should not count to any
        // stats at all".
        _db.PreviousPlayersInGame.Add(
            new PreviousPlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = cancelledGame.Id,
                UserId = user.Id,
                Reason = PlayerReplacementReason.Vote,
            }
        );

        await _db.SaveChangesAsync();

        var result = await _sut.RecalculateAsync(user.Id);

        Assert.NotNull(result);
        Assert.Equal(0, result.WonGamesCount);
        Assert.Equal(0, result.FinishedGamesCount);
        Assert.Equal(0, result.RemovedFromGameCount);
        Assert.Null(result.WinRate);
    }

    [Fact]
    public async Task FinishedGamesCount_ExcludesGamesLeftEarly_ButWinRateDenominatorIncludesThem()
    {
        var user = new ApplicationUser { UserName = "mixed_guy", Email = "m@example.com" };
        await _userManager.CreateAsync(user);

        var wonGame = new Game
        {
            Id = Guid.NewGuid(),
            Name = "Won",
            OwnerUserId = user.Id,
            State = GameState.Finished,
            ViewOfGame = Json("""{"settings": {"setupId": "base-game"}}"""),
        };
        var leftEarlyGame = new Game
        {
            Id = Guid.NewGuid(),
            Name = "Left early but finished",
            OwnerUserId = user.Id,
            State = GameState.Finished,
            ViewOfGame = Json("""{"settings": {"setupId": "base-game"}}"""),
        };
        _db.Games.AddRange(wonGame, leftEarlyGame);

        _db.PlayersInGame.Add(
            new PlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = wonGame.Id,
                UserId = user.Id,
                Data = Json("""{"house": "stark", "is_winner": true}"""),
            }
        );
        _db.PreviousPlayersInGame.Add(
            new PreviousPlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = leftEarlyGame.Id,
                UserId = user.Id,
                Reason = PlayerReplacementReason.ClockTimeout,
            }
        );

        await _db.SaveChangesAsync();

        var result = await _sut.RecalculateAsync(user.Id);

        Assert.NotNull(result);
        Assert.Equal(1, result.WonGamesCount);
        // "Finished games" is only the 1 game actually played to the end - the left-early game is
        // NOT included here even though it's factored into the win rate below.
        Assert.Equal(1, result.FinishedGamesCount);
        Assert.Equal(1, result.RemovedFromGameCount);
        // Win rate's denominator is 2 (1 win + 1 unconditional loss from the left-early game).
        Assert.Equal(0.5, result.WinRate);
    }

    [Fact]
    public async Task FinishedGamesCount_ExcludesFacelessGames_ButIncludesTutorialGames()
    {
        var user = new ApplicationUser { UserName = "faceless_guy", Email = "f@example.com" };
        await _userManager.CreateAsync(user);

        // Faceless games hide who's playing which house entirely - Pages.UserModel's games list
        // drops them outright, so "Finished games" must match that and drop them too, keeping the
        // "Games badge == Ongoing + Finished" invariant intact.
        var facelessGame = new Game
        {
            Id = Guid.NewGuid(),
            Name = "Faceless",
            OwnerUserId = user.Id,
            State = GameState.Finished,
            ViewOfGame = Json("""{"settings": {"setupId": "base-game", "faceless": true}}"""),
        };
        // The tutorial variant is excluded from the win-rate percentage but must still count as a
        // plain Finished game for the "Finished games" badge - only the games list's own faceless
        // exclusion is mirrored here, not the win-rate percentage's narrower exclusions.
        var tutorialGame = new Game
        {
            Id = Guid.NewGuid(),
            Name = "Tutorial",
            OwnerUserId = user.Id,
            State = GameState.Finished,
            ViewOfGame = Json("""{"settings": {"setupId": "learn-the-game"}}"""),
        };
        _db.Games.AddRange(facelessGame, tutorialGame);

        _db.PlayersInGame.AddRange(
            new PlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = facelessGame.Id,
                UserId = user.Id,
                Data = Json("""{"house": "stark"}"""),
            },
            new PlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = tutorialGame.Id,
                UserId = user.Id,
                Data = Json("""{"house": "stark", "is_winner": true}"""),
            }
        );

        await _db.SaveChangesAsync();

        var result = await _sut.RecalculateAsync(user.Id);

        Assert.NotNull(result);
        // Only the tutorial game counts - the faceless one is dropped entirely.
        Assert.Equal(1, result.FinishedGamesCount);
        // The tutorial game is excluded from the win-rate percentage though, so there's nothing
        // left to compute a percentage from.
        Assert.Null(result.WinRate);
    }

    public void Dispose()
    {
        _db.Dispose();
        _provider.Dispose();
    }
}
