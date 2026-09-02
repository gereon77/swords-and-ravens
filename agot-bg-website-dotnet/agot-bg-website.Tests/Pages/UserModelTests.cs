using System.Security.Claims;
using System.Text.Json;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Auth;
using agot_bg_website.Pages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace agot_bg_website.Tests.Pages;

/// <summary>
/// Covers the public user-profile page (MIGRATION_PLAN.md §13/§14): 404 for a soft-deleted
/// ("Took the Black") user, and the win-rate/games-list computation over PlayerInGame's opaque
/// JSON payload for an anonymous viewer.
/// </summary>
public class UserModelTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthorizationService _authorizationService;

    public UserModelTests()
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
        services.AddAuthorizationBuilder().AddGamePermissionPolicies();

        _provider = services.BuildServiceProvider();
        _db = _provider.GetRequiredService<ApplicationDbContext>();
        _userManager = _provider.GetRequiredService<UserManager<ApplicationUser>>();
        _authorizationService = _provider.GetRequiredService<IAuthorizationService>();
    }

    private UserModel CreatePageModel(ClaimsPrincipal? viewer = null)
    {
        var model = new UserModel(_db, _userManager, _authorizationService)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = viewer ?? new ClaimsPrincipal(new ClaimsIdentity()),
                },
            },
        };
        return model;
    }

    private static JsonDocument Json(string json) => JsonDocument.Parse(json);

    [Fact]
    public async Task DeletedUser_Returns404()
    {
        var user = new ApplicationUser
        {
            UserName = "deleted-guy",
            Email = "deleted@example.com",
            IsDeleted = true,
        };
        await _userManager.CreateAsync(user);

        var result = await CreatePageModel().OnGetAsync(user.Id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task NonexistentUser_Returns404()
    {
        var result = await CreatePageModel().OnGetAsync(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ComputesWinRateAndGameListFromPlayerInGameData()
    {
        var user = new ApplicationUser { UserName = "robb_stark", Email = "robb@example.com" };
        await _userManager.CreateAsync(user);

        var wonGame = new Game
        {
            Id = Guid.NewGuid(),
            Name = "A finished game (won)",
            OwnerUserId = user.Id,
            State = GameState.Finished,
            ViewOfGame = Json(
                """{"turn": 10, "maxPlayerCount": 6, "winner": "stark", "settings": {"setupId": "base-game"}}"""
            ),
        };
        var lostGame = new Game
        {
            Id = Guid.NewGuid(),
            Name = "A finished game (lost)",
            OwnerUserId = user.Id,
            State = GameState.Finished,
            ViewOfGame = Json(
                """{"turn": 10, "maxPlayerCount": 6, "winner": "lannister", "settings": {"setupId": "base-game"}}"""
            ),
        };
        var learnTheGame = new Game
        {
            Id = Guid.NewGuid(),
            Name = "Tutorial game",
            OwnerUserId = user.Id,
            State = GameState.Finished,
            ViewOfGame = Json(
                """{"turn": 10, "maxPlayerCount": 6, "winner": "stark", "settings": {"setupId": "learn-the-game"}}"""
            ),
        };
        var facelessGame = new Game
        {
            Id = Guid.NewGuid(),
            Name = "Faceless game",
            OwnerUserId = user.Id,
            State = GameState.Finished,
            ViewOfGame = Json(
                """{"turn": 10, "maxPlayerCount": 6, "settings": {"setupId": "base-game", "faceless": true}}"""
            ),
        };
        var ongoingGame = new Game
        {
            Id = Guid.NewGuid(),
            Name = "Ongoing game",
            OwnerUserId = user.Id,
            State = GameState.Ongoing,
            ViewOfGame = Json(
                """{"turn": 3, "maxPlayerCount": 6, "waitingFor": "Stark", "settings": {"setupId": "base-game"}}"""
            ),
        };
        var cancelledGame = new Game
        {
            Id = Guid.NewGuid(),
            Name = "Cancelled game",
            OwnerUserId = user.Id,
            State = GameState.Cancelled,
            ViewOfGame = Json(
                """{"turn": 1, "maxPlayerCount": 6, "settings": {"setupId": "base-game"}}"""
            ),
        };
        _db.Games.AddRange(
            wonGame,
            lostGame,
            learnTheGame,
            facelessGame,
            ongoingGame,
            cancelledGame
        );

        _db.PlayersInGame.AddRange(
            new PlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = wonGame.Id,
                UserId = user.Id,
                Data = Json("""{"house": "stark", "is_winner": true}"""),
            },
            new PlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = lostGame.Id,
                UserId = user.Id,
                Data = Json("""{"house": "stark", "is_winner": false}"""),
            },
            new PlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = learnTheGame.Id,
                UserId = user.Id,
                Data = Json("""{"house": "stark", "is_winner": true}"""),
            },
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
                GameId = ongoingGame.Id,
                UserId = user.Id,
                Data = Json("""{"house": "stark"}"""),
            },
            new PlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = cancelledGame.Id,
                UserId = user.Id,
                Data = Json("""{"house": "stark"}"""),
            }
        );

        // A previous stint that ended in a finished game - always a loss (MIGRATION_PLAN.md §10.2).
        _db.PreviousPlayersInGame.Add(
            new PreviousPlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = wonGame.Id,
                UserId = user.Id,
                House = "greyjoy",
                SequenceNumber = 0,
                Reason = PlayerReplacementReason.Vote,
            }
        );

        await _db.SaveChangesAsync();

        var model = CreatePageModel();
        var result = await model.OnGetAsync(user.Id);

        Assert.IsType<PageResult>(result);
        // wonGame + lostGame count towards stats (1 win, 1 loss); learnTheGame and facelessGame are
        // excluded entirely; plus 1 unconditional loss from the PreviousPlayerInGame row.
        Assert.Equal(1, model.WonCount);
        Assert.Equal(3, model.FinishedCount);
        Assert.Equal(1, model.RemovedFromGameCount);
        Assert.Equal("33.3 %", model.WinRateDisplay);
        Assert.Equal(1, model.OngoingCount);

        // Faceless games are hidden from the games list entirely; the rest (won/lost/tutorial/
        // ongoing) show up, cancelled games are listed separately.
        Assert.Equal(4, model.GamesOfUser.Count);
        Assert.DoesNotContain(model.GamesOfUser, g => g.GameId == facelessGame.Id);
        Assert.Single(model.CancelledGames);
        Assert.Equal(cancelledGame.Id, model.CancelledGames[0].GameId);
    }

    public void Dispose()
    {
        _db.Dispose();
        _provider.Dispose();
    }
}
